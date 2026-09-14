using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using BiTanEnergyApi.Data;
using BiTanEnergyApi.Dtos;
using BiTanEnergyApi.Models;

namespace BiTanEnergyApi.Controllers;

[ApiController]
[Route("api/auth")]
[Authorize]
public class AuthController : ControllerBase
{
    private readonly MongoContext _db;
    private static readonly PasswordHasher<AdminUser> Hasher = new();

    public AuthController(MongoContext db)
    {
        _db = db;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        var user = await _db.AdminUsers.Find(u => u.Username == req.Username).FirstOrDefaultAsync();
        if (user == null)
            return Unauthorized(new { message = "帳號或密碼錯誤" });

        var result = Hasher.VerifyHashedPassword(user, user.PasswordHash, req.Password);
        if (result == PasswordVerificationResult.Failed)
            return Unauthorized(new { message = "帳號或密碼錯誤" });

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Role, user.Role),
            new("uid", user.Id.ToString())
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.AddDays(14) });

        return Ok(new MeResponse { Username = user.Username, Role = user.Role, AssignedGroups = user.AssignedGroups });
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Ok();
    }

    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        if (User.Identity == null || !User.Identity.IsAuthenticated)
            return Unauthorized();

        // 每次都查最新的門市指派，這樣管理員改了指派後，使用者不用重新登入就會生效。
        var uid = User.FindFirstValue("uid");
        var user = await _db.AdminUsers.Find(u => u.Id == uid).FirstOrDefaultAsync();
        if (user == null) return Unauthorized();

        return Ok(new MeResponse { Username = user.Username, Role = user.Role, AssignedGroups = user.AssignedGroups });
    }

    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest req)
    {
        var uid = User.FindFirstValue("uid");
        if (string.IsNullOrEmpty(uid)) return Unauthorized();

        var user = await _db.AdminUsers.Find(u => u.Id == uid).FirstOrDefaultAsync();
        if (user == null) return Unauthorized();

        var result = Hasher.VerifyHashedPassword(user, user.PasswordHash, req.CurrentPassword);
        if (result == PasswordVerificationResult.Failed)
            return BadRequest(new { message = "目前密碼不正確" });

        if (string.IsNullOrWhiteSpace(req.NewPassword) || req.NewPassword.Length < 6)
            return BadRequest(new { message = "新密碼至少需要 6 個字元" });

        user.PasswordHash = Hasher.HashPassword(user, req.NewPassword);
        await _db.AdminUsers.ReplaceOneAsync(u => u.Id == uid, user);
        return Ok();
    }
}
