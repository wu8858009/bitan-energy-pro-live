using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BiTanEnergyApi.Data;
using BiTanEnergyApi.Dtos;
using BiTanEnergyApi.Models;

namespace BiTanEnergyApi.Controllers;

[ApiController]
[Route("api/sites")]
[Authorize]
public class SitesController : ControllerBase
{
    private readonly AppDbContext _db;

    public SitesController(AppDbContext db)
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
        var sites = await _db.Sites.OrderBy(s => s.Id).ToListAsync();
        return Ok(sites.Select(ToDto));
    }

    [HttpPost]
    public async Task<ActionResult<SiteDto>> Create([FromBody] SiteUpsertRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Site))
            return BadRequest(new { message = "請輸入站點名稱" });

        var site = new Site
        {
            Group = req.Group,
            Name = req.Site,
            Location = req.Location,
            MeterNo = req.MeterNo,
            Type = req.Type,
            BasePrev = req.BasePrev
        };
        _db.Sites.Add(site);
        await _db.SaveChangesAsync();
        return Ok(ToDto(site));
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<SiteDto>> Update(int id, [FromBody] SiteUpsertRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Site))
            return BadRequest(new { message = "請輸入站點名稱" });

        var site = await _db.Sites.FindAsync(id);
        if (site == null) return NotFound();

        site.Group = req.Group;
        site.Name = req.Site;
        site.Location = req.Location;
        site.MeterNo = req.MeterNo;
        site.Type = req.Type;
        site.BasePrev = req.BasePrev;
        await _db.SaveChangesAsync();
        return Ok(ToDto(site));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var site = await _db.Sites.FindAsync(id);
        if (site == null) return NotFound();
        _db.Sites.Remove(site);
        await _db.SaveChangesAsync();
        return Ok();
    }
}
