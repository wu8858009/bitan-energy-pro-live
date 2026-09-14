using System.Security.Claims;
using MongoDB.Driver;
using BiTanEnergyApi.Data;

namespace BiTanEnergyApi.Services;

public static class AccessControl
{
    // 回傳這個使用者能看到/管理的門市（Site.Group）清單；null 代表不受限（Admin）。
    // 每次都查一次資料庫（不是從登入時的 cookie claim 讀），這樣管理員改了指派門市後，
    // 使用者不用重新登入就會立刻生效。
    public static async Task<List<string>?> GetAllowedGroupsAsync(ClaimsPrincipal user, MongoContext db)
    {
        if (user.FindFirstValue(ClaimTypes.Role) == "Admin") return null;

        var uid = user.FindFirstValue("uid");
        if (string.IsNullOrEmpty(uid)) return new List<string>();

        var account = await db.AdminUsers.Find(u => u.Id == uid).FirstOrDefaultAsync();
        return account?.AssignedGroups ?? new List<string>();
    }
}
