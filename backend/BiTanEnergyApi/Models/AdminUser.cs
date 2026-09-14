using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace BiTanEnergyApi.Models;

public class AdminUser
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = "";
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = "Admin";

    // 僅 Role="User" 時有效：限制這個帳號只能看到/管理哪些門市（對應 Site.Group）。
    // Admin 一律不受限，不看這個欄位。
    public List<string> AssignedGroups { get; set; } = new();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
