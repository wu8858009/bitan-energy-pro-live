using BiTanEnergyApi.Data;
using BiTanEnergyApi.Models;
using MongoDB.Bson;

namespace BiTanEnergyApi.Services;

public static class AuditLogger
{
    public static async Task LogAsync(MongoContext db, string actorUsername, string action, string detail)
    {
        await db.AuditLogs.InsertOneAsync(new AuditLog
        {
            Id = ObjectId.GenerateNewId().ToString(),
            ActorUsername = actorUsername,
            Action = action,
            Detail = detail,
            CreatedAt = DateTime.UtcNow
        });
    }
}
