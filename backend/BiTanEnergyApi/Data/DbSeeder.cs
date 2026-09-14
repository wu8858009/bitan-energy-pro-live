using Microsoft.AspNetCore.Identity;
using MongoDB.Bson;
using MongoDB.Driver;
using BiTanEnergyApi.Models;

namespace BiTanEnergyApi.Data;

public static class DbSeeder
{
    public static async Task SeedAdminAsync(MongoContext db, IConfiguration config)
    {
        if (await db.AdminUsers.Find(_ => true).AnyAsync()) return;

        var username = config["Admin:InitialUsername"] ?? "admin";
        var password = config["Admin:InitialPassword"];
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException(
                "尚未設定 Admin:InitialPassword（appsettings 或環境變數 Admin__InitialPassword），無法建立第一個管理員帳號。");
        }

        var user = new AdminUser { Id = ObjectId.GenerateNewId().ToString(), Username = username };
        var hasher = new PasswordHasher<AdminUser>();
        user.PasswordHash = hasher.HashPassword(user, password);

        await db.AdminUsers.InsertOneAsync(user);
    }
}
