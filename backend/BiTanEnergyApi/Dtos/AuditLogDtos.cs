namespace BiTanEnergyApi.Dtos;

public class AuditLogDto
{
    public string Id { get; set; } = "";
    public string ActorUsername { get; set; } = "";
    public string Action { get; set; } = "";
    public string Detail { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
