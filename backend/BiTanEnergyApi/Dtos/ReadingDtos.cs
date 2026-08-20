namespace BiTanEnergyApi.Dtos;

public class PhotoDto
{
    public int Id { get; set; }
    public string Url { get; set; } = "";
}

public class ReadingDto
{
    public int SiteId { get; set; }
    public decimal? Curr { get; set; }
    public List<PhotoDto> Photos { get; set; } = new();
}

public class ReadingUpsertRequest
{
    public decimal? Curr { get; set; }
}

public class AllReadingDto
{
    public int SiteId { get; set; }
    public string MonthKey { get; set; } = "";
    public decimal? Curr { get; set; }
    public List<PhotoDto> Photos { get; set; } = new();
}
