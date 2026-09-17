namespace BiTanEnergyApi.Dtos;

public class DailyReadingDto
{
    public string Date { get; set; } = "";
    public decimal Value { get; set; }
}

public class DailyReadingUpsertRequest
{
    public decimal Value { get; set; }
}
