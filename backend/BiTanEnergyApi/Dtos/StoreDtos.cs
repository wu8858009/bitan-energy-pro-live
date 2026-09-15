namespace BiTanEnergyApi.Dtos;

public class StoreDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int SiteCount { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class StoreCreateRequest
{
    public string Name { get; set; } = "";
}

public class StoreUpdateRequest
{
    public string Name { get; set; } = "";
}
