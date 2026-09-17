using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace BiTanEnergyApi.Models;

public class AdminUser
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = "";
    public string Username { get; set; } = "";
    // 給帳號管理列表顯示用的姓名，跟登入用的 Username 分開——列表只顯示這個，不顯示帳號本身。
    // 舊資料沒有這個欄位時預設空字串，前端會退回顯示 Username。
    public string DisplayName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = "Admin";

    // 僅 Role="User" 時有效：限制這個帳號只能看到/管理哪些門市（對應 Site.Group）。
    // Admin 一律不受限，不看這個欄位。
    public List<string> AssignedGroups { get; set; } = new();

    // 僅 Role="User" 時有效："View"(只看) | "Edit"(可新增/修改) | "Full"(可新增/修改/刪除)。
    // Admin 一律視為 Full，不看這個欄位。預設 Full 是為了不影響這個欄位新增之前就存在的帳號。
    public string PermissionLevel { get; set; } = "Full";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // 登入安全：連續輸入錯誤密碼達到門檻就鎖定帳號一段時間，防止暴力猜密碼。
    // 登入成功會重設回 0 / null。
    public int FailedLoginCount { get; set; } = 0;
    public DateTime? LockedUntil { get; set; }
}
