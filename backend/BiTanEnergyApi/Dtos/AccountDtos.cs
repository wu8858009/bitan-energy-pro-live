namespace BiTanEnergyApi.Dtos;

public class AccountDto
{
    public string Id { get; set; } = "";
    public string Username { get; set; } = "";
    public string Role { get; set; } = "";
    public List<string> AssignedGroups { get; set; } = new();
    public string PermissionLevel { get; set; } = "Full"; // "View" | "Edit" | "Full"
    public DateTime CreatedAt { get; set; }
}

public class AccountCreateRequest
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string Role { get; set; } = "User"; // "Admin" | "User"
    public List<string> AssignedGroups { get; set; } = new();
    public string PermissionLevel { get; set; } = "Full";
}

public class AccountUpdateRequest
{
    public string Username { get; set; } = "";
    // 留空表示不修改密碼
    public string? NewPassword { get; set; }
    public string Role { get; set; } = "User";
    public List<string> AssignedGroups { get; set; } = new();
    public string PermissionLevel { get; set; } = "Full";
}
