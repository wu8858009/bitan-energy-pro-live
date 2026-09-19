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
using BiTanEnergyApi.Services;

namespace BiTanEnergyApi.Controllers;

[ApiController]
[Route("api/auth")]
[Authorize]
public class AuthController : ControllerBase
{
    // 登入安全設定：連續輸入錯誤密碼達到這個次數就鎖定帳號一段時間，防止暴力猜密碼。
    private const int MaxFailedAttempts = 5;
    private const int LockoutMinutes = 15;

    private readonly MongoContext _db;
    private static readonly PasswordHasher<AdminUser> Hasher = new();

    public AuthController(MongoContext db)
    {
        _db = db;
    }

    private static MeResponse ToMeResponse(AdminUser u) => new()
    {
        Username = u.Username,
        DisplayName = u.DisplayName,
        Role = u.Role,
        AssignedGroups = u.AssignedGroups,
        PermissionLevel = u.Role == "Admin" ? AccessControl.PermissionFull : u.PermissionLevel
    };

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        var user = await _db.AdminUsers.Find(u => u.Username == req.Username).FirstOrDefaultAsync();
        if (user == null)
        {
            await AuditLogger.LogAsync(_db, req.Username, "登入失敗", "帳號不存在");
            return Unauthorized(new { message = "帳號或密碼錯誤" });
        }

        if (user.LockedUntil.HasValue && user.LockedUntil.Value > DateTime.UtcNow)
        {
            var minutesLeft = (int)Math.Ceiling((user.LockedUntil.Value - DateTime.UtcNow).TotalMinutes);
            return Unauthorized(new { message = $"密碼錯誤次數過多，帳號已鎖定，請 {minutesLeft} 分鐘後再試" });
        }

        var result = Hasher.VerifyHashedPassword(user, user.PasswordHash, req.Password);
        if (result == PasswordVerificationResult.Failed)
        {
            user.FailedLoginCount += 1;
            string message;
            if (user.FailedLoginCount >= MaxFailedAttempts)
            {
                user.LockedUntil = DateTime.UtcNow.AddMinutes(LockoutMinutes);
                user.FailedLoginCount = 0;
                message = $"密碼錯誤次數過多，帳號已鎖定 {LockoutMinutes} 分鐘";
                await AuditLogger.LogAsync(_db, user.Username, "帳號鎖定", $"連續輸入錯誤密碼 {MaxFailedAttempts} 次，鎖定 {LockoutMinutes} 分鐘");
            }
            else
            {
                message = "帳號或密碼錯誤";
            }
            await _db.AdminUsers.ReplaceOneAsync(u => u.Id == user.Id, user);
            await AuditLogger.LogAsync(_db, user.Username, "登入失敗", "密碼錯誤");
            return Unauthorized(new { message });
        }

        if (user.FailedLoginCount > 0 || user.LockedUntil.HasValue)
        {
            user.FailedLoginCount = 0;
            user.LockedUntil = null;
            await _db.AdminUsers.ReplaceOneAsync(u => u.Id == user.Id, user);
        }

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

        await _db.AdminUsers.UpdateOneAsync(u => u.Id == user.Id,
            Builders<AdminUser>.Update.Set(u => u.LastSeenAt, DateTime.UtcNow));
        await AuditLogger.LogAsync(_db, user.Username, "登入成功", "");
        return Ok(ToMeResponse(user));
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

        return Ok(ToMeResponse(user));
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
        await AuditLogger.LogAsync(_db, user.Username, "修改密碼", "");
        return Ok();
    }

    // 前端登入後每分鐘呼叫一次，記錄最後在線時間，管理員在帳號管理看得到誰目前在線上。
    [HttpPost("heartbeat")]
    public async Task<IActionResult> Heartbeat()
    {
        var uid = User.FindFirstValue("uid");
        if (string.IsNullOrEmpty(uid)) return Unauthorized();
        await _db.AdminUsers.UpdateOneAsync(u => u.Id == uid,
            Builders<AdminUser>.Update.Set(u => u.LastSeenAt, DateTime.UtcNow));
        return Ok();
    }

    // 讓使用者（任何角色）自己改自己的姓名——不用麻煩管理員代為修改。
    [HttpPut("display-name")]
    public async Task<IActionResult> UpdateDisplayName([FromBody] UpdateDisplayNameRequest req)
    {
        var uid = User.FindFirstValue("uid");
        if (string.IsNullOrEmpty(uid)) return Unauthorized();

        var user = await _db.AdminUsers.Find(u => u.Id == uid).FirstOrDefaultAsync();
        if (user == null) return Unauthorized();

        user.DisplayName = (req.DisplayName ?? "").Trim();
        await _db.AdminUsers.ReplaceOneAsync(u => u.Id == uid, user);
        return Ok(ToMeResponse(user));
    }
}
