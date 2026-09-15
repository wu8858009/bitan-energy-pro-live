using MongoDB.Bson;
using MongoDB.Driver;
using BiTanEnergyApi.Models;

namespace BiTanEnergyApi.Data;

// 一次性（但每次啟動都安全重跑）的搬遷：把既有站點裡已經在用、但從未被正式「建立」成
// Store 紀錄的群組名稱，自動補進 Stores 集合，這樣它們才會出現在門市管理／帳號指派清單裡，
// 不會因為這次改版而突然消失。
public static class StoreBackfill
{
    public static async Task EnsureExistingGroupsAsync(MongoContext db)
    {
        var groupNames = await db.Sites.Distinct(s => s.Group, FilterDefinition<Site>.Empty).ToListAsync();
        foreach (var name in groupNames)
        {
            await EnsureStoreExistsAsync(db, name);
        }
    }

    // 新增/修改站點時，如果打的群組名稱還沒有對應的門市紀錄，就地補上一筆，
    // 這樣門市清單（帳號指派、首頁篩選）永遠跟站點實際在用的群組同步，不用等下次重啟。
    public static async Task EnsureStoreExistsAsync(MongoContext db, string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        var exists = await db.Stores.Find(s => s.Name == name).AnyAsync();
        if (exists) return;

        try
        {
            await db.Stores.InsertOneAsync(new Store { Id = ObjectId.GenerateNewId().ToString(), Name = name });
        }
        catch (MongoWriteException)
        {
            // 罕見的併發情況：幾乎同時有另一個請求剛好插入了同名門市，忽略即可。
        }
    }
}
