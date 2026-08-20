using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BiTanEnergyApi.Data;
using BiTanEnergyApi.Dtos;
using BiTanEnergyApi.Models;
using BiTanEnergyApi.Services;

namespace BiTanEnergyApi.Controllers;

[ApiController]
[Route("api/backup")]
// [Authorize] — 暫時移除登入驗證，需要恢復時把這行取消註解
public class BackupController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _config;

    public BackupController(AppDbContext db, IWebHostEnvironment env, IConfiguration config)
    {
        _db = db;
        _env = env;
        _config = config;
    }

    [HttpGet("export")]
    public async Task<ActionResult<BackupPayload>> Export()
    {
        var sites = await _db.Sites.OrderBy(s => s.Id).ToListAsync();
        var readings = await _db.MonthlyReadings.ToListAsync();

        var payload = new BackupPayload
        {
            Sites = sites.Select(s => new SiteDto
            {
                Id = s.Id,
                Group = s.Group,
                Site = s.Name,
                Location = s.Location,
                MeterNo = s.MeterNo,
                Type = s.Type,
                BasePrev = s.BasePrev
            }).ToList(),
            Readings = readings.Select(r => new BackupReadingEntry
            {
                SiteId = r.SiteId,
                MonthKey = r.MonthKey,
                Curr = r.CurrentValue
            }).ToList()
        };
        return Ok(payload);
    }

    // 還原備份：覆蓋所有站點與各月讀數（不含照片檔案，照片仍保留於伺服器）
    [HttpPost("import")]
    public async Task<IActionResult> Import([FromBody] BackupPayload payload)
    {
        if (payload?.Sites == null) return BadRequest(new { message = "備份格式不正確" });

        using var tx = await _db.Database.BeginTransactionAsync();

        _db.MonthlyReadings.RemoveRange(_db.MonthlyReadings);
        _db.Sites.RemoveRange(_db.Sites);
        await _db.SaveChangesAsync();

        var idMap = new Dictionary<int, int>();
        foreach (var s in payload.Sites)
        {
            var site = new Site
            {
                Group = s.Group,
                Name = s.Site,
                Location = s.Location,
                MeterNo = s.MeterNo,
                Type = s.Type,
                BasePrev = s.BasePrev
            };
            _db.Sites.Add(site);
            await _db.SaveChangesAsync();
            idMap[s.Id] = site.Id;
        }

        foreach (var r in payload.Readings ?? new List<BackupReadingEntry>())
        {
            if (!idMap.TryGetValue(r.SiteId, out var newSiteId)) continue;
            _db.MonthlyReadings.Add(new MonthlyReading
            {
                SiteId = newSiteId,
                MonthKey = r.MonthKey,
                CurrentValue = r.Curr
            });
        }
        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        return Ok();
    }

    // POST /api/backup/clear-all — 清空所有站點與所有月份讀數／照片（管理員帳號不受影響）
    [HttpPost("clear-all")]
    public async Task<IActionResult> ClearAll()
    {
        var uploadsRoot = UploadsPathResolver.Resolve(_env, _config);
        var filePaths = await _db.ReadingPhotos.Select(p => p.FilePath).ToListAsync();

        _db.MonthlyReadings.RemoveRange(_db.MonthlyReadings);
        _db.Sites.RemoveRange(_db.Sites);
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
}
