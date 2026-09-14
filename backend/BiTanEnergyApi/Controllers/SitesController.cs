using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;
using BiTanEnergyApi.Data;
using BiTanEnergyApi.Dtos;
using BiTanEnergyApi.Models;

namespace BiTanEnergyApi.Controllers;

[ApiController]
[Route("api/sites")]
[Authorize]
public class SitesController : ControllerBase
{
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
        BasePrev = s.BasePrev
    };

    [HttpGet]
    public async Task<ActionResult<List<SiteDto>>> GetAll()
    {
        var sites = await _db.Sites.Find(_ => true).SortBy(s => s.Id).ToListAsync();
        return Ok(sites.Select(ToDto));
    }

    [HttpPost]
    public async Task<ActionResult<SiteDto>> Create([FromBody] SiteUpsertRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Site))
            return BadRequest(new { message = "請輸入站點名稱" });

        var site = new Site
        {
            Id = ObjectId.GenerateNewId().ToString(),
            Group = req.Group,
            Name = req.Site,
            Location = req.Location,
            MeterNo = req.MeterNo,
            Type = req.Type,
            BasePrev = req.BasePrev
        };
        await _db.Sites.InsertOneAsync(site);
        return Ok(ToDto(site));
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<SiteDto>> Update(string id, [FromBody] SiteUpsertRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Site))
            return BadRequest(new { message = "請輸入站點名稱" });

        var site = await _db.Sites.Find(s => s.Id == id).FirstOrDefaultAsync();
        if (site == null) return NotFound();

        site.Group = req.Group;
        site.Name = req.Site;
        site.Location = req.Location;
        site.MeterNo = req.MeterNo;
        site.Type = req.Type;
        site.BasePrev = req.BasePrev;
        await _db.Sites.ReplaceOneAsync(s => s.Id == id, site);
        return Ok(ToDto(site));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var site = await _db.Sites.Find(s => s.Id == id).FirstOrDefaultAsync();
        if (site == null) return NotFound();

        // Mongo has no FK cascade — clean up the site's readings (and their embedded
        // photos) explicitly. Note: this does not delete the photos' physical files,
        // matching the previous EF behavior (it never touched disk on site delete either).
        await _db.MonthlyReadings.DeleteManyAsync(r => r.SiteId == id);
        await _db.Sites.DeleteOneAsync(s => s.Id == id);
        return Ok();
    }
}
