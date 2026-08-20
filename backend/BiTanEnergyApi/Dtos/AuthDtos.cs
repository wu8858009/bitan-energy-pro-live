namespace BiTanEnergyApi.Dtos;

public class LoginRequest
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}

public class ChangePasswordRequest
{
    public string CurrentPassword { get; set; } = "";
    public string NewPassword { get; set; } = "";
}

public class MeResponse
{
    public string Username { get; set; } = "";
    public string Role { get; set; } = "";
}
