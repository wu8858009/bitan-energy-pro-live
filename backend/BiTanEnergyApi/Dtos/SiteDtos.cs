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
    public int MeterDigits { get; set; } = 5;
}

public class SiteUpsertRequest
{
    public string Group { get; set; } = "";
    public string Site { get; set; } = "";
    public string Location { get; set; } = "";
    public string MeterNo { get; set; } = "";
    public string Type { get; set; } = "water";
    public decimal BasePrev { get; set; }
    public int MeterDigits { get; set; } = 5;
}
