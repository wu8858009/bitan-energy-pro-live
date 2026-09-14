using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace BiTanEnergyApi.Models;

// Embedded inside MonthlyReading.Photos — not its own collection.
public class ReadingPhoto
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = "";

    // Relative path under the configured uploads root
    public string FilePath { get; set; } = "";
    public string ContentType { get; set; } = "image/jpeg";
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
}
