namespace BiTanEnergyApi.Models;

public class MonthlyReading
{
    public int Id { get; set; }
    public int SiteId { get; set; }
    public Site? Site { get; set; }

    // "YYYY-MM"
    public string MonthKey { get; set; } = "";

    public decimal? CurrentValue { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<ReadingPhoto> Photos { get; set; } = new();
}
