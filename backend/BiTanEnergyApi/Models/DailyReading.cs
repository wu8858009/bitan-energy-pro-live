using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace BiTanEnergyApi.Models;

// 每日讀數：跟月度的「上期/本期」完全獨立、互不影響，純粹讓使用者在同一個月裡
// 額外針對某幾天記一筆錶盤讀數，用來觀察月中的用量趨勢；月結算仍然只看 MonthlyReading。
public class DailyReading
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = "";
    public string SiteId { get; set; } = "";
    public string Date { get; set; } = ""; // "YYYY-MM-DD"
    public decimal Value { get; set; }
}
