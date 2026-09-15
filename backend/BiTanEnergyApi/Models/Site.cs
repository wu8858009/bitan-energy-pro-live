using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace BiTanEnergyApi.Models;

public class Site
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = "";
    public string Group { get; set; } = "";
    public string Name { get; set; } = "";
    public string Location { get; set; } = "";
    public string MeterNo { get; set; } = "";
    public string Type { get; set; } = "water"; // water | elec | gas
    public decimal BasePrev { get; set; }

    // 錶盤位數（幾位數會翻回 0），給「上期讀數智慧補正」用來判斷是否發生翻轉。
    public int MeterDigits { get; set; } = 4;
}
