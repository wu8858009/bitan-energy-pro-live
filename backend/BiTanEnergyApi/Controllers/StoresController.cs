using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;
using BiTanEnergyApi.Data;
using BiTanEnergyApi.Dtos;
using BiTanEnergyApi.Models;

namespace BiTanEnergyApi.Controllers;

// 門市管理：只有 Admin 能新增/重新命名/刪除門市。
[ApiController]
[Route("api/stores")]
[Authorize(Roles = "Admin")]
public class StoresController : ControllerBase
{
    private readonly MongoContext _db;

    public StoresController(MongoContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<List<StoreDto>>> GetAll()
    {
        var stores = await _db.Stores.Find(_ => true).SortBy(s => s.Name).ToListAsync();
        var sites = await _db.Sites.Find(_ => true).ToListAsync();
        var countByGroup = sites.GroupBy(s => s.Group).ToDictionary(g => g.Key, g => g.Count());

        return Ok(stores.Select(s => new StoreDto
        {
            Id = s.Id,
            Name = s.Name,
            CreatedAt = s.CreatedAt,
            SiteCount = countByGroup.GetValueOrDefault(s.Name, 0)
        }));
    }

    [HttpPost]
    public async Task<ActionResult<StoreDto>> Create([FromBody] StoreCreateRequest req)
    {
        var name = (req.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(new { message = "請輸入門市名稱" });

        var exists = await _db.Stores.Find(s => s.Name == name).AnyAsync();
        if (exists) return BadRequest(new { message = "這個門市名稱已經存在" });

        var store = new Store { Id = ObjectId.GenerateNewId().ToString(), Name = name };
        await _db.Stores.InsertOneAsync(store);
        return Ok(new StoreDto { Id = store.Id, Name = store.Name, CreatedAt = store.CreatedAt, SiteCount = 0 });
    }

    // 重新命名門市：連帶更新底下站點的 Group，以及有指派到這個門市的帳號的 AssignedGroups，
    // 不然改完名字，站點跟帳號權限就跟舊名字對不起來了。
    [HttpPut("{id}")]
    public async Task<ActionResult<StoreDto>> Update(string id, [FromBody] StoreUpdateRequest req)
    {
        var newName = (req.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(newName))
            return BadRequest(new { message = "請輸入門市名稱" });

        var store = await _db.Stores.Find(s => s.Id == id).FirstOrDefaultAsync();
        if (store == null) return NotFound();

        if (newName != store.Name)
        {
            var nameTaken = await _db.Stores.Find(s => s.Name == newName && s.Id != id).AnyAsync();
            if (nameTaken) return BadRequest(new { message = "這個門市名稱已經存在" });

            var oldName = store.Name;
            store.Name = newName;
            await _db.Stores.ReplaceOneAsync(s => s.Id == id, store);

            await _db.Sites.UpdateManyAsync(s => s.Group == oldName,
                Builders<Site>.Update.Set(s => s.Group, newName));

            var affectedUsers = await _db.AdminUsers.Find(u => u.AssignedGroups.Contains(oldName)).ToListAsync();
            foreach (var u in affectedUsers)
            {
                var groups = u.AssignedGroups.Select(g => g == oldName ? newName : g).ToList();
                await _db.AdminUsers.UpdateOneAsync(x => x.Id == u.Id,
                    Builders<AdminUser>.Update.Set(x => x.AssignedGroups, groups));
            }
        }

        var siteCount = (int)await _db.Sites.CountDocumentsAsync(s => s.Group == store.Name);
        return Ok(new StoreDto { Id = store.Id, Name = store.Name, CreatedAt = store.CreatedAt, SiteCount = siteCount });
    }

    // 刪除門市：底下還有站點的話先擋下來，避免一次搞丟一堆站點資料。
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var store = await _db.Stores.Find(s => s.Id == id).FirstOrDefaultAsync();
        if (store == null) return NotFound();

        var siteCount = await _db.Sites.CountDocumentsAsync(s => s.Group == store.Name);
        if (siteCount > 0)
            return BadRequest(new { message = $"這個門市底下還有 {siteCount} 個站點，請先刪除或改指派這些站點的群組後再刪除門市" });

        await _db.Stores.DeleteOneAsync(s => s.Id == id);

        var affectedUsers = await _db.AdminUsers.Find(u => u.AssignedGroups.Contains(store.Name)).ToListAsync();
        foreach (var u in affectedUsers)
        {
            var groups = u.AssignedGroups.Where(g => g != store.Name).ToList();
            await _db.AdminUsers.UpdateOneAsync(x => x.Id == u.Id,
                Builders<AdminUser>.Update.Set(x => x.AssignedGroups, groups));
        }

        return Ok();
    }
}
