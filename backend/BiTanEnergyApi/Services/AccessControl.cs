using System.Security.Claims;
using MongoDB.Driver;
using BiTanEnergyApi.Data;

namespace BiTanEnergyApi.Services;

public static class AccessControl
{
    public const string PermissionView = "View";
    public const string PermissionEdit = "Edit";
    public const string PermissionFull = "Full";

    private static readonly Dictionary<string, int> PermissionRank = new()
    {
        [PermissionView] = 0,
        [PermissionEdit] = 1,
        [PermissionFull] = 2,
    };

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

    // Admin 一律 Full；User 角色回傳帳號上設定的等級（View/Edit/Full）。
    public static async Task<string> GetPermissionLevelAsync(ClaimsPrincipal user, MongoContext db)
    {
        if (user.FindFirstValue(ClaimTypes.Role) == "Admin") return PermissionFull;

        var uid = user.FindFirstValue("uid");
        if (string.IsNullOrEmpty(uid)) return PermissionView;

        var account = await db.AdminUsers.Find(u => u.Id == uid).FirstOrDefaultAsync();
        return account?.PermissionLevel ?? PermissionView;
    }

    // 是否至少有 required 這個等級的權限（Full > Edit > View）。
    public static async Task<bool> HasPermissionAsync(ClaimsPrincipal user, MongoContext db, string required)
    {
        var level = await GetPermissionLevelAsync(user, db);
        return PermissionRank.GetValueOrDefault(level, 0) >= PermissionRank.GetValueOrDefault(required, 0);
    }
}
