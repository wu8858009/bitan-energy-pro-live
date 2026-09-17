using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace BiTanEnergyApi.Models;

// 操作日誌：記錄「誰在什麼時候做了什麼」，只涵蓋結構性/管理性的變動（站點、帳號、門市、
// 備份還原/清除、登入安全事件），不記錄每一筆抄表讀數的輸入——那種頻率太高，記了也沒人會看，
// 反而把真正重要的管理操作淹沒掉。
public class AuditLog
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = "";
    public string ActorUsername { get; set; } = "";
    public string Action { get; set; } = "";
    public string Detail { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
