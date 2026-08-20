using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BiTanEnergyApi.Data;
using BiTanEnergyApi.Dtos;
using BiTanEnergyApi.Models;
using BiTanEnergyApi.Services;

namespace BiTanEnergyApi.Controllers;

[ApiController]
[Route("api")]
// [Authorize] — 暫時移除登入驗證，需要恢復時把這行取消註解
public class ReadingsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _config;

    public ReadingsController(AppDbContext db, IWebHostEnvironment env, IConfiguration config)
    {
        _db = db;
        _env = env;
        _config = config;
    }

    private string UploadsRoot() => UploadsPathResolver.Resolve(_env, _config);

    private static bool IsValidMonthKey(string monthKey) =>
        System.Text.RegularExpressions.Regex.IsMatch(monthKey ?? "", @"^\d{4}-(0[1-9]|1[0-2])$");

    private static ReadingDto ToDto(int siteId, MonthlyReading? r) => new()
    {
        SiteId = siteId,
        Curr = r?.CurrentValue,
        Photos = r?.Photos.OrderBy(p => p.Id)
            .Select(p => new PhotoDto { Id = p.Id, Url = $"/api/photos/{p.Id}" })
            .ToList() ?? new List<PhotoDto>()
    };

    // GET /api/readings?month=YYYY-MM
    [HttpGet("readings")]
    public async Task<ActionResult<List<ReadingDto>>> GetByMonth([FromQuery] string month)
    {
        if (!IsValidMonthKey(month)) return BadRequest(new { message = "月份格式錯誤" });

        var siteIds = await _db.Sites.Select(s => s.Id).ToListAsync();
        var readings = await _db.MonthlyReadings
            .Include(r => r.Photos)
            .Where(r => r.MonthKey == month)
            .ToListAsync();
        var bySite = readings.ToDictionary(r => r.SiteId);

        var result = siteIds.Select(id => ToDto(id, bySite.GetValueOrDefault(id))).ToList();
        return Ok(result);
    }

    // GET /api/readings/all — 一次取回所有月份的讀數（給前端開機時整批快取，取代舊版全部塞在 localStorage 的做法）
    [HttpGet("readings/all")]
    public async Task<ActionResult<List<AllReadingDto>>> GetAll()
    {
        var readings = await _db.MonthlyReadings.Include(r => r.Photos).ToListAsync();
        var result = readings.Select(r => new AllReadingDto
        {
            SiteId = r.SiteId,
            MonthKey = r.MonthKey,
            Curr = r.CurrentValue,
            Photos = r.Photos.OrderBy(p => p.Id)
                .Select(p => new PhotoDto { Id = p.Id, Url = $"/api/photos/{p.Id}" })
                .ToList()
        }).ToList();
        return Ok(result);
    }

    // DELETE /api/readings?month=YYYY-MM — 清除單一月份所有站點的讀數與照片，站點本身保留
    [HttpDelete("readings")]
    public async Task<IActionResult> DeleteMonth([FromQuery] string month)
    {
        if (!IsValidMonthKey(month)) return BadRequest(new { message = "月份格式錯誤" });

        var readings = await _db.MonthlyReadings
            .Include(r => r.Photos)
            .Where(r => r.MonthKey == month)
            .ToListAsync();

        var uploadsRoot = UploadsRoot();
        var filePaths = readings.SelectMany(r => r.Photos).Select(p => p.FilePath).ToList();

        _db.MonthlyReadings.RemoveRange(readings);
        await _db.SaveChangesAsync();

        foreach (var relPath in filePaths)
        {
            var absPath = Path.Combine(uploadsRoot, relPath.Replace('/', Path.DirectorySeparatorChar));
            if (System.IO.File.Exists(absPath))
            {
                try { System.IO.File.Delete(absPath); } catch { /* best-effort cleanup */ }
            }
        }
        return Ok();
    }

    // PUT /api/readings/{siteId}?month=YYYY-MM
    [HttpPut("readings/{siteId}")]
    public async Task<ActionResult<ReadingDto>> Upsert(int siteId, [FromQuery] string month, [FromBody] ReadingUpsertRequest req)
    {
        if (!IsValidMonthKey(month)) return BadRequest(new { message = "月份格式錯誤" });
        var siteExists = await _db.Sites.AnyAsync(s => s.Id == siteId);
        if (!siteExists) return NotFound();

        var reading = await _db.MonthlyReadings.Include(r => r.Photos)
            .FirstOrDefaultAsync(r => r.SiteId == siteId && r.MonthKey == month);
        if (reading == null)
        {
            reading = new MonthlyReading { SiteId = siteId, MonthKey = month };
            _db.MonthlyReadings.Add(reading);
        }
        reading.CurrentValue = req.Curr;
        reading.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Ok(ToDto(siteId, reading));
    }

    // POST /api/readings/{siteId}/photos?month=YYYY-MM  (multipart/form-data, field "file")
    [HttpPost("readings/{siteId}/photos")]
    [RequestSizeLimit(20_000_000)]
    public async Task<ActionResult<PhotoDto>> UploadPhoto(int siteId, [FromQuery] string month, IFormFile file)
    {
        if (!IsValidMonthKey(month)) return BadRequest(new { message = "月份格式錯誤" });
        if (file == null || file.Length == 0) return BadRequest(new { message = "沒有收到照片檔案" });

        var allowed = new[] { "image/jpeg", "image/png", "image/webp", "image/heic" };
        if (!allowed.Contains(file.ContentType))
            return BadRequest(new { message = "不支援的圖片格式" });

        var siteExists = await _db.Sites.AnyAsync(s => s.Id == siteId);
        if (!siteExists) return NotFound();

        var reading = await _db.MonthlyReadings
            .FirstOrDefaultAsync(r => r.SiteId == siteId && r.MonthKey == month);
        if (reading == null)
        {
            reading = new MonthlyReading { SiteId = siteId, MonthKey = month };
            _db.MonthlyReadings.Add(reading);
            await _db.SaveChangesAsync();
        }

        var ext = file.ContentType switch
        {
            "image/png" => ".png",
            "image/webp" => ".webp",
            "image/heic" => ".heic",
            _ => ".jpg"
        };
        var relativeDir = Path.Combine(siteId.ToString(), month);
        var absDir = Path.Combine(UploadsRoot(), relativeDir);
        Directory.CreateDirectory(absDir);
        var fileName = $"{Guid.NewGuid():N}{ext}";
        var absPath = Path.Combine(absDir, fileName);

        await using (var stream = System.IO.File.Create(absPath))
        {
            await file.CopyToAsync(stream);
        }

        var photo = new ReadingPhoto
        {
            MonthlyReadingId = reading.Id,
            FilePath = Path.Combine(relativeDir, fileName).Replace('\\', '/'),
            ContentType = file.ContentType
        };
        _db.ReadingPhotos.Add(photo);
        await _db.SaveChangesAsync();

        return Ok(new PhotoDto { Id = photo.Id, Url = $"/api/photos/{photo.Id}" });
    }

    // GET /api/photos/{photoId}
    [HttpGet("photos/{photoId}")]
    public async Task<IActionResult> GetPhoto(int photoId)
    {
        var photo = await _db.ReadingPhotos.FindAsync(photoId);
        if (photo == null) return NotFound();

        var absPath = Path.Combine(UploadsRoot(), photo.FilePath.Replace('/', Path.DirectorySeparatorChar));
        if (!System.IO.File.Exists(absPath)) return NotFound();

        var stream = System.IO.File.OpenRead(absPath);
        return File(stream, photo.ContentType);
    }

    // DELETE /api/photos/{photoId}
    [HttpDelete("photos/{photoId}")]
    public async Task<IActionResult> DeletePhoto(int photoId)
    {
        var photo = await _db.ReadingPhotos.FindAsync(photoId);
        if (photo == null) return NotFound();

        var absPath = Path.Combine(UploadsRoot(), photo.FilePath.Replace('/', Path.DirectorySeparatorChar));
        _db.ReadingPhotos.Remove(photo);
        await _db.SaveChangesAsync();

        if (System.IO.File.Exists(absPath))
        {
            try { System.IO.File.Delete(absPath); } catch { /* best-effort cleanup */ }
        }
        return Ok();
    }
}
