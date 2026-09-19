using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;
using BiTanEnergyApi.Data;
using BiTanEnergyApi.Dtos;
using BiTanEnergyApi.Models;
using BiTanEnergyApi.Services;

namespace BiTanEnergyApi.Controllers;

// 帳號管理：只有 Admin 角色能新增/刪除/修改帳號，一般 User 角色登入後看不到、也打不進這些端點。
[ApiController]
[Route("api/accounts")]
[Authorize(Roles = "Admin")]
public class AccountsController : ControllerBase
{
    private static readonly string[] AllowedRoles = { "Admin", "User" };
    private static readonly string[] AllowedPermissionLevels =
        { AccessControl.PermissionView, AccessControl.PermissionEdit, AccessControl.PermissionFull };
    private readonly MongoContext _db;
    private static readonly PasswordHasher<AdminUser> Hasher = new();

    public AccountsController(MongoContext db)
    {
        _db = db;
    }

    private static AccountDto ToDto(AdminUser u) => new()
    {
        Id = u.Id,
        DisplayName = u.DisplayName,
        Username = u.Username,
        Role = u.Role,
        AssignedGroups = u.AssignedGroups,
        PermissionLevel = u.Role == "Admin" ? AccessControl.PermissionFull : u.PermissionLevel,
        CreatedAt = u.CreatedAt,
        LockedUntil = u.LockedUntil,
        LastSeenAt = u.LastSeenAt,
        IsOnline = u.LastSeenAt.HasValue && (DateTime.UtcNow - u.LastSeenAt.Value).TotalSeconds < 45
    };

    [HttpGet]
    public async Task<ActionResult<List<AccountDto>>> GetAll()
    {
        var users = await _db.AdminUsers.Find(_ => true).SortBy(u => u.CreatedAt).ToListAsync();
        return Ok(users.Select(ToDto));
    }

    [HttpPost]
    public async Task<ActionResult<AccountDto>> Create([FromBody] AccountCreateRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Username))
            return BadRequest(new { message = "請輸入帳號名稱" });
        if (string.IsNullOrWhiteSpace(req.Password) || req.Password.Length < 6)
            return BadRequest(new { message = "密碼至少需要 6 個字元" });
        if (!AllowedRoles.Contains(req.Role))
            return BadRequest(new { message = "角色不正確" });
        if (!AllowedPermissionLevels.Contains(req.PermissionLevel))
            return BadRequest(new { message = "權限等級不正確" });

        var exists = await _db.AdminUsers.Find(u => u.Username == req.Username).AnyAsync();
        if (exists) return BadRequest(new { message = "這個帳號名稱已經存在" });

        var user = new AdminUser
        {
            Id = ObjectId.GenerateNewId().ToString(),
            DisplayName = (req.DisplayName ?? "").Trim(),
            Username = req.Username,
            Role = req.Role,
            AssignedGroups = req.AssignedGroups ?? new List<string>(),
            PermissionLevel = req.Role == "Admin" ? AccessControl.PermissionFull : req.PermissionLevel
        };
        user.PasswordHash = Hasher.HashPassword(user, req.Password);
        await _db.AdminUsers.InsertOneAsync(user);
        await AuditLogger.LogAsync(_db, User.Identity?.Name ?? "", "新增帳號", user.Username);

        return Ok(ToDto(user));
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<AccountDto>> Update(string id, [FromBody] AccountUpdateRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Username))
            return BadRequest(new { message = "請輸入帳號名稱" });
        if (!AllowedRoles.Contains(req.Role))
            return BadRequest(new { message = "角色不正確" });
        if (!AllowedPermissionLevels.Contains(req.PermissionLevel))
            return BadRequest(new { message = "權限等級不正確" });

        var user = await _db.AdminUsers.Find(u => u.Id == id).FirstOrDefaultAsync();
        if (user == null) return NotFound();

        user.DisplayName = (req.DisplayName ?? "").Trim();

        if (req.Username != user.Username)
        {
            var exists = await _db.AdminUsers.Find(u => u.Username == req.Username && u.Id != id).AnyAsync();
            if (exists) return BadRequest(new { message = "這個帳號名稱已經存在" });
            user.Username = req.Username;
        }

        if (!string.IsNullOrWhiteSpace(req.NewPassword))
        {
            if (req.NewPassword.Length < 6)
                return BadRequest(new { message = "密碼至少需要 6 個字元" });
            user.PasswordHash = Hasher.HashPassword(user, req.NewPassword);
        }

        if (user.Role == "Admin" && req.Role != "Admin")
        {
            var adminCount = await _db.AdminUsers.CountDocumentsAsync(u => u.Role == "Admin");
            if (adminCount <= 1)
                return BadRequest(new { message = "至少需要保留一個管理員帳號" });
        }
        user.Role = req.Role;
        user.AssignedGroups = req.AssignedGroups ?? new List<string>();
        user.PermissionLevel = req.Role == "Admin" ? AccessControl.PermissionFull : req.PermissionLevel;

        await _db.AdminUsers.ReplaceOneAsync(u => u.Id == id, user);
        await AuditLogger.LogAsync(_db, User.Identity?.Name ?? "", "修改帳號", user.Username);
        return Ok(ToDto(user));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var currentUid = User.FindFirstValue("uid");
        if (id == currentUid)
            return BadRequest(new { message = "無法刪除自己目前登入中的帳號" });

        var user = await _db.AdminUsers.Find(u => u.Id == id).FirstOrDefaultAsync();
        if (user == null) return NotFound();

        if (user.Role == "Admin")
        {
            var adminCount = await _db.AdminUsers.CountDocumentsAsync(u => u.Role == "Admin");
            if (adminCount <= 1)
                return BadRequest(new { message = "至少需要保留一個管理員帳號" });
        }

        await _db.AdminUsers.DeleteOneAsync(u => u.Id == id);
        await AuditLogger.LogAsync(_db, User.Identity?.Name ?? "", "刪除帳號", user.Username);
        return Ok();
    }

    // POST /api/accounts/{id}/unlock — 手動解除密碼錯誤鎖定，不用等 15 分鐘
    [HttpPost("{id}/unlock")]
    public async Task<IActionResult> Unlock(string id)
    {
        var user = await _db.AdminUsers.Find(u => u.Id == id).FirstOrDefaultAsync();
        if (user == null) return NotFound();

        user.FailedLoginCount = 0;
        user.LockedUntil = null;
        await _db.AdminUsers.ReplaceOneAsync(u => u.Id == id, user);
        await AuditLogger.LogAsync(_db, User.Identity?.Name ?? "", "手動解鎖帳號", user.Username);
        return Ok(ToDto(user));
    }
}
