namespace BiTanEnergyApi.Models;

public class Site
{
    public int Id { get; set; }
    public string Group { get; set; } = "";
    public string Name { get; set; } = "";
    public string Location { get; set; } = "";
    public string MeterNo { get; set; } = "";
    public string Type { get; set; } = "water"; // water | elec | gas
    public decimal BasePrev { get; set; }

    public List<MonthlyReading> Readings { get; set; } = new();
}
