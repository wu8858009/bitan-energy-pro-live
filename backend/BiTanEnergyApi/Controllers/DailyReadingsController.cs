using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;
using BiTanEnergyApi.Data;
using BiTanEnergyApi.Dtos;
using BiTanEnergyApi.Models;
using BiTanEnergyApi.Services;

namespace BiTanEnergyApi.Controllers;

// 每日讀數：獨立於月度「上期/本期」之外的補充記錄，純粹給使用者觀察月中用量趨勢用，
// 權限模型（誰能看/改哪個站點）跟 ReadingsController 一致。
[ApiController]
[Route("api/daily-readings")]
[Authorize]
public class DailyReadingsController : ControllerBase
{
    private readonly MongoContext _db;

    public DailyReadingsController(MongoContext db)
    {
        _db = db;
    }

    private static bool IsValidDate(string date) =>
        System.Text.RegularExpressions.Regex.IsMatch(date ?? "", @"^\d{4}-\d{2}-\d{2}$");

    private static bool IsValidMonthKey(string monthKey) =>
        System.Text.RegularExpressions.Regex.IsMatch(monthKey ?? "", @"^\d{4}-(0[1-9]|1[0-2])$");

    private async Task<bool> CanAccessSiteAsync(string siteId)
    {
        var allowedGroups = await AccessControl.GetAllowedGroupsAsync(User, _db);
        if (allowedGroups == null) return true;
        var site = await _db.Sites.Find(s => s.Id == siteId).FirstOrDefaultAsync();
        return site != null && allowedGroups.Contains(site.Group);
    }

    // GET /api/daily-readings/{siteId}?month=YYYY-MM
    [HttpGet("{siteId}")]
    public async Task<ActionResult<List<DailyReadingDto>>> GetForMonth(string siteId, [FromQuery] string month)
    {
        if (!IsValidMonthKey(month)) return BadRequest(new { message = "月份格式錯誤" });
        if (!await CanAccessSiteAsync(siteId)) return Forbid();

        var prefix = month + "-";
        var readings = await _db.DailyReadings
            .Find(r => r.SiteId == siteId && r.Date.StartsWith(prefix))
            .SortBy(r => r.Date)
            .ToListAsync();

        return Ok(readings.Select(r => new DailyReadingDto { Date = r.Date, Value = r.Value }).ToList());
    }

    // PUT /api/daily-readings/{siteId}?date=YYYY-MM-DD
    [HttpPut("{siteId}")]
    public async Task<ActionResult<DailyReadingDto>> Upsert(string siteId, [FromQuery] string date, [FromBody] DailyReadingUpsertRequest req)
    {
        if (!IsValidDate(date)) return BadRequest(new { message = "日期格式錯誤" });
        if (!await CanAccessSiteAsync(siteId)) return Forbid();
        if (!await AccessControl.HasPermissionAsync(User, _db, AccessControl.PermissionEdit)) return Forbid();

        var filter = Builders<DailyReading>.Filter.Where(r => r.SiteId == siteId && r.Date == date);
        var update = Builders<DailyReading>.Update
            .Set(r => r.Value, req.Value)
            .SetOnInsert(r => r.Id, ObjectId.GenerateNewId().ToString())
            .SetOnInsert(r => r.SiteId, siteId)
            .SetOnInsert(r => r.Date, date);

        var reading = await _db.DailyReadings.FindOneAndUpdateAsync(filter, update,
            new FindOneAndUpdateOptions<DailyReading> { IsUpsert = true, ReturnDocument = ReturnDocument.After });

        return Ok(new DailyReadingDto { Date = reading.Date, Value = reading.Value });
    }

    // DELETE /api/daily-readings/{siteId}?date=YYYY-MM-DD — 清掉手誤記錯的單日記錄
    [HttpDelete("{siteId}")]
    public async Task<IActionResult> Delete(string siteId, [FromQuery] string date)
    {
        if (!IsValidDate(date)) return BadRequest(new { message = "日期格式錯誤" });
        if (!await CanAccessSiteAsync(siteId)) return Forbid();
        if (!await AccessControl.HasPermissionAsync(User, _db, AccessControl.PermissionEdit)) return Forbid();

        await _db.DailyReadings.DeleteOneAsync(r => r.SiteId == siteId && r.Date == date);
        return Ok();
    }
}
