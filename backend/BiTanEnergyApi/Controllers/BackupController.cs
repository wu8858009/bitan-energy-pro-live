using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;
using BiTanEnergyApi.Data;
using BiTanEnergyApi.Dtos;
using BiTanEnergyApi.Models;
using BiTanEnergyApi.Services;

namespace BiTanEnergyApi.Controllers;

[ApiController]
[Route("api/backup")]
[Authorize]
public class BackupController : ControllerBase
{
    private readonly MongoContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _config;

    public BackupController(MongoContext db, IWebHostEnvironment env, IConfiguration config)
    {
        _db = db;
        _env = env;
        _config = config;
    }

    [HttpGet("export")]
    public async Task<ActionResult<BackupPayload>> Export()
    {
        var sites = await _db.Sites.Find(_ => true).SortBy(s => s.Id).ToListAsync();
        var readings = await _db.MonthlyReadings.Find(_ => true).ToListAsync();

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

    // 還原備份：覆蓋所有站點與各月讀數（不含照片檔案，照片仍保留於伺服器 —
    // 既有設計：還原後舊的讀數文件連同內嵌照片一併被整批刪除重建，照片實體檔案會變成孤兒，
    // 這是原本 EF 版本就有的限制，沿用不修改）
    [HttpPost("import")]
    public async Task<IActionResult> Import([FromBody] BackupPayload payload)
    {
        if (payload?.Sites == null) return BadRequest(new { message = "備份格式不正確" });

        using var session = await _db.Client.StartSessionAsync();
        session.StartTransaction();
        try
        {
            await _db.MonthlyReadings.DeleteManyAsync(session, FilterDefinition<MonthlyReading>.Empty);
            await _db.Sites.DeleteManyAsync(session, FilterDefinition<Site>.Empty);

            var idMap = new Dictionary<string, string>();
            foreach (var s in payload.Sites)
            {
                var site = new Site
                {
                    Id = ObjectId.GenerateNewId().ToString(),
                    Group = s.Group,
                    Name = s.Site,
                    Location = s.Location,
                    MeterNo = s.MeterNo,
                    Type = s.Type,
                    BasePrev = s.BasePrev
                };
                await _db.Sites.InsertOneAsync(session, site);
                idMap[s.Id] = site.Id;
            }

            var newReadings = new List<MonthlyReading>();
            foreach (var r in payload.Readings ?? new List<BackupReadingEntry>())
            {
                if (!idMap.TryGetValue(r.SiteId, out var newSiteId)) continue;
                newReadings.Add(new MonthlyReading
                {
                    Id = ObjectId.GenerateNewId().ToString(),
                    SiteId = newSiteId,
                    MonthKey = r.MonthKey,
                    CurrentValue = r.Curr
                });
            }
            if (newReadings.Count > 0)
                await _db.MonthlyReadings.InsertManyAsync(session, newReadings);

            await session.CommitTransactionAsync();
        }
        catch
        {
            await session.AbortTransactionAsync();
            throw;
        }

        return Ok();
    }

    // POST /api/backup/clear-all — 清空所有站點與所有月份讀數／照片（管理員帳號不受影響）
    [HttpPost("clear-all")]
    public async Task<IActionResult> ClearAll()
    {
        var uploadsRoot = UploadsPathResolver.Resolve(_env, _config);
        var readings = await _db.MonthlyReadings.Find(_ => true).ToListAsync();
        var filePaths = readings.SelectMany(r => r.Photos).Select(p => p.FilePath).ToList();

        await _db.MonthlyReadings.DeleteManyAsync(FilterDefinition<MonthlyReading>.Empty);
        await _db.Sites.DeleteManyAsync(FilterDefinition<Site>.Empty);

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
