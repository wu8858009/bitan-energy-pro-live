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

    // BasePrev 只當作「BaseMonth 那個月」的上期基準值；別的月份如果還沒有真正的讀數鏈
    // (上個月的 curr)，就不會回退用 BasePrev，改成「沒有資料」。null 代表這個站點是舊資料
    // （這個欄位加入之前就存在），維持舊行為：BasePrev 對任何沒有讀數鏈的月份都適用，
    // 避免這次改版讓既有站點的計算結果無聲跑掉。
    public string? BaseMonth { get; set; }

    // 錶盤位數（幾位數會翻回 0），給「上期讀數智慧補正」用來判斷是否發生翻轉。
    public int MeterDigits { get; set; } = 4;
}
