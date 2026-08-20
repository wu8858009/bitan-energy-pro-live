namespace BiTanEnergyApi.Dtos;

public class BackupReadingEntry
{
    public int SiteId { get; set; }
    public string MonthKey { get; set; } = "";
    public decimal? Curr { get; set; }
}

public class BackupPayload
{
    public List<SiteDto> Sites { get; set; } = new();
    public List<BackupReadingEntry> Readings { get; set; } = new();
}
