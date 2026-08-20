namespace BiTanEnergyApi.Models;

public class ReadingPhoto
{
    public int Id { get; set; }
    public int MonthlyReadingId { get; set; }
    public MonthlyReading? MonthlyReading { get; set; }

    // Relative path under the configured uploads root
    public string FilePath { get; set; } = "";
    public string ContentType { get; set; } = "image/jpeg";
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
}
