namespace BiTanEnergyApi.Dtos;

public class SiteDto
{
    public string Id { get; set; } = "";
    public string Group { get; set; } = "";
    public string Site { get; set; } = "";
    public string Location { get; set; } = "";
    public string MeterNo { get; set; } = "";
    public string Type { get; set; } = "water";
    public decimal BasePrev { get; set; }
    public string? BaseMonth { get; set; }
    public int MeterDigits { get; set; } = 4;
}

public class SiteUpsertRequest
{
    public string Group { get; set; } = "";
    public string Site { get; set; } = "";
    public string Location { get; set; } = "";
    public string MeterNo { get; set; } = "";
    public string Type { get; set; } = "water";
    public decimal BasePrev { get; set; }
    // 前端存目前選定的月份（YYYY-MM），只有在 BasePrev 真的被改動時才會拿來更新 BaseMonth。
    public string? BaseMonth { get; set; }
    public int MeterDigits { get; set; } = 4;
}
