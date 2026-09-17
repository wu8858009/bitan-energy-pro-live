using BiTanEnergyApi.Models;
using MongoDB.Driver;

namespace BiTanEnergyApi.Data;

// Replaces db.Database.Migrate(). Index creation is idempotent, safe to run on every startup.
public static class MongoIndexInitializer
{
    public static async Task EnsureIndexesAsync(MongoContext db)
    {
        var readingKeys = Builders<MonthlyReading>.IndexKeys
            .Ascending(r => r.SiteId)
            .Ascending(r => r.MonthKey);
        await db.MonthlyReadings.Indexes.CreateOneAsync(
            new CreateIndexModel<MonthlyReading>(readingKeys, new CreateIndexOptions { Unique = true }));

        // Lets GetPhoto/DeletePhoto look up an embedded photo by its own id without a month/site hint.
        var photoIdKeys = Builders<MonthlyReading>.IndexKeys.Ascending("Photos._id");
        await db.MonthlyReadings.Indexes.CreateOneAsync(new CreateIndexModel<MonthlyReading>(photoIdKeys));

        var usernameKeys = Builders<AdminUser>.IndexKeys.Ascending(u => u.Username);
        await db.AdminUsers.Indexes.CreateOneAsync(
            new CreateIndexModel<AdminUser>(usernameKeys, new CreateIndexOptions { Unique = true }));

        var storeNameKeys = Builders<Store>.IndexKeys.Ascending(s => s.Name);
        await db.Stores.Indexes.CreateOneAsync(
            new CreateIndexModel<Store>(storeNameKeys, new CreateIndexOptions { Unique = true }));

        var dailyReadingKeys = Builders<DailyReading>.IndexKeys
            .Ascending(r => r.SiteId)
            .Ascending(r => r.Date);
        await db.DailyReadings.Indexes.CreateOneAsync(
            new CreateIndexModel<DailyReading>(dailyReadingKeys, new CreateIndexOptions { Unique = true }));

        var auditLogKeys = Builders<AuditLog>.IndexKeys.Descending(a => a.CreatedAt);
        await db.AuditLogs.Indexes.CreateOneAsync(new CreateIndexModel<AuditLog>(auditLogKeys));
    }
}
