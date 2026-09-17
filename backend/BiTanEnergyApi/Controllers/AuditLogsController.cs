using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using BiTanEnergyApi.Data;
using BiTanEnergyApi.Dtos;

namespace BiTanEnergyApi.Controllers;

// 操作日誌：只有 Admin 看得到，記錄的是結構性/管理性的變動（見 AuditLog 模型註解）。
[ApiController]
[Route("api/audit-logs")]
[Authorize(Roles = "Admin")]
public class AuditLogsController : ControllerBase
{
    private readonly MongoContext _db;

    public AuditLogsController(MongoContext db)
    {
        _db = db;
    }

    // GET /api/audit-logs?limit=100
    [HttpGet]
    public async Task<ActionResult<List<AuditLogDto>>> GetAll([FromQuery] int limit = 100)
    {
        var take = Math.Clamp(limit, 1, 500);
        var logs = await _db.AuditLogs.Find(_ => true)
            .SortByDescending(a => a.CreatedAt)
            .Limit(take)
            .ToListAsync();

        return Ok(logs.Select(a => new AuditLogDto
        {
            Id = a.Id,
            ActorUsername = a.ActorUsername,
            Action = a.Action,
            Detail = a.Detail,
            CreatedAt = a.CreatedAt
        }));
    }
}
