using BiTanEnergyApi.Models;
using MongoDB.Driver;

namespace BiTanEnergyApi.Data;

// Wraps the MongoClient/IMongoDatabase. Registered as a singleton — MongoClient
// manages its own connection pool and is meant to be long-lived, unlike the
// scoped-per-request AppDbContext this replaces.
public class MongoContext
{
    public MongoClient Client { get; }
    public IMongoDatabase Database { get; }

    public IMongoCollection<Site> Sites { get; }
    public IMongoCollection<MonthlyReading> MonthlyReadings { get; }
    public IMongoCollection<AdminUser> AdminUsers { get; }

    public MongoContext(IConfiguration config)
    {
        var connectionString = config["MongoDb:ConnectionString"];
        var databaseName = config["MongoDb:DatabaseName"];
        if (string.IsNullOrWhiteSpace(connectionString) || string.IsNullOrWhiteSpace(databaseName))
        {
            throw new InvalidOperationException(
                "尚未設定 MongoDb:ConnectionString / MongoDb:DatabaseName（appsettings 或環境變數 MongoDb__ConnectionString / MongoDb__DatabaseName）。");
        }

        Client = new MongoClient(connectionString);
        Database = Client.GetDatabase(databaseName);

        Sites = Database.GetCollection<Site>("sites");
        MonthlyReadings = Database.GetCollection<MonthlyReading>("monthlyReadings");
        AdminUsers = Database.GetCollection<AdminUser>("adminUsers");
    }
}
