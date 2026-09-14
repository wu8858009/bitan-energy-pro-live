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
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<ReadingPhoto> Photos { get; set; } = new();
}
