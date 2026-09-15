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
[Route("api/sites")]
[Authorize]
public class SitesController : ControllerBase
{
    private static readonly int[] AllowedMeterDigits = { 4, 5, 6 };
    private readonly MongoContext _db;

    public SitesController(MongoContext db)
    {
        _db = db;
    }

    private static SiteDto ToDto(Site s) => new()
    {
        Id = s.Id,
        Group = s.Group,
        Site = s.Name,
        Location = s.Location,
        MeterNo = s.MeterNo,
        Type = s.Type,
        BasePrev = s.BasePrev,
        BaseMonth = s.BaseMonth,
        MeterDigits = s.MeterDigits
    };

    [HttpGet]
    public async Task<ActionResult<List<SiteDto>>> GetAll()
    {
        var allowedGroups = await AccessControl.GetAllowedGroupsAsync(User, _db);
        var filter = allowedGroups == null
            ? FilterDefinition<Site>.Empty
            : Builders<Site>.Filter.In(s => s.Group, allowedGroups);

        var sites = await _db.Sites.Find(filter).SortBy(s => s.Id).ToListAsync();
        return Ok(sites.Select(ToDto));
    }

    [HttpPost]
    public async Task<ActionResult<SiteDto>> Create([FromBody] SiteUpsertRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Site))
            return BadRequest(new { message = "請輸入站點名稱" });
        if (!AllowedMeterDigits.Contains(req.MeterDigits))
            return BadRequest(new { message = "儀表位數不正確" });

        if (!await AccessControl.HasPermissionAsync(User, _db, AccessControl.PermissionEdit))
            return Forbid();

        var allowedGroups = await AccessControl.GetAllowedGroupsAsync(User, _db);
        if (allowedGroups != null && !allowedGroups.Contains(req.Group))
            return Forbid();

        var site = new Site
        {
            Id = ObjectId.GenerateNewId().ToString(),
            Group = req.Group,
            Name = req.Site,
            Location = req.Location,
            MeterNo = req.MeterNo,
            Type = req.Type,
            BasePrev = req.BasePrev,
            BaseMonth = req.BaseMonth,
            MeterDigits = req.MeterDigits
        };
        await _db.Sites.InsertOneAsync(site);
        return Ok(ToDto(site));
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<SiteDto>> Update(string id, [FromBody] SiteUpsertRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Site))
            return BadRequest(new { message = "請輸入站點名稱" });
        if (!AllowedMeterDigits.Contains(req.MeterDigits))
            return BadRequest(new { message = "儀表位數不正確" });

        if (!await AccessControl.HasPermissionAsync(User, _db, AccessControl.PermissionEdit))
            return Forbid();

        var site = await _db.Sites.Find(s => s.Id == id).FirstOrDefaultAsync();
        if (site == null) return NotFound();

        var allowedGroups = await AccessControl.GetAllowedGroupsAsync(User, _db);
        if (allowedGroups != null && (!allowedGroups.Contains(site.Group) || !allowedGroups.Contains(req.Group)))
            return Forbid();

        site.Group = req.Group;
        site.Name = req.Site;
        site.Location = req.Location;
        site.MeterNo = req.MeterNo;
        site.Type = req.Type;
        // 只有「上期讀數」這個值真的被改動時，才把 BaseMonth 移到現在選定的月份——
        // 單純改站名、錶號之類不相關的欄位，不該悄悄改變這個基準值原本代表的月份。
        if (site.BasePrev != req.BasePrev)
        {
            site.BaseMonth = req.BaseMonth;
        }
        site.BasePrev = req.BasePrev;
        site.MeterDigits = req.MeterDigits;
        await _db.Sites.ReplaceOneAsync(s => s.Id == id, site);
        return Ok(ToDto(site));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        if (!await AccessControl.HasPermissionAsync(User, _db, AccessControl.PermissionFull))
            return Forbid();

        var site = await _db.Sites.Find(s => s.Id == id).FirstOrDefaultAsync();
        if (site == null) return NotFound();

        var allowedGroups = await AccessControl.GetAllowedGroupsAsync(User, _db);
        if (allowedGroups != null && !allowedGroups.Contains(site.Group))
            return Forbid();

        // Mongo has no FK cascade — clean up the site's readings (and their embedded
        // photos) explicitly. Note: this does not delete the photos' physical files,
        // matching the previous EF behavior (it never touched disk on site delete either).
        await _db.MonthlyReadings.DeleteManyAsync(r => r.SiteId == id);
        await _db.Sites.DeleteOneAsync(s => s.Id == id);
        return Ok();
    }
}
