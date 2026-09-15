using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace BiTanEnergyApi.Models;

public class MonthlyReading
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = "";
    public string SiteId { get; set; } = "";

    // "YYYY-MM"
    public string MonthKey { get; set; } = "";

    public decimal? CurrentValue { get; set; }

    // 這個月專屬的「上期」覆蓋值：設定後只影響這個月自己的用量計算，
    // 不會動到上個月實際存的讀數（上個月自己的本期顯示不受影響）。null 表示沒有覆蓋，
    // 照原本規則用上個月的 CurrentValue 或 Site.BasePrev 當上期。
    public decimal? PrevOverride { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<ReadingPhoto> Photos { get; set; } = new();
}
