using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace BiTanEnergyApi.Models;

// 門市（對應 Site.Group 這個自由文字欄位）。獨立成一個集合，這樣可以先建立一個
// 還沒有任何站點的空門市，之後再指派給帳號或建立站點時挑選，不用先有站點才算數。
public class Store
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
