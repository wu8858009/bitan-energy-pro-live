using Microsoft.AspNetCore.Authentication.Cookies;
using BiTanEnergyApi.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers().AddJsonOptions(o =>
{
    o.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSingleton<MongoContext>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "BiTanEnergyAuth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromDays(14);
        options.SlidingExpiration = true;
        // This is an API, not an MVC login page — return status codes instead of redirecting.
        options.Events.OnRedirectToLogin = ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();

// Dev convenience only: lets index.html opened directly via file:// (Origin: null)
// reach the local dotnet run backend; production (Render, same-origin) never uses this.
builder.Services.AddCors(options =>
{
    options.AddPolicy("DevFileOrigin", policy =>
        policy.SetIsOriginAllowed(_ => true).AllowAnyHeader().AllowAnyMethod().AllowCredentials());
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var mongo = scope.ServiceProvider.GetRequiredService<MongoContext>();
    await MongoIndexInitializer.EnsureIndexesAsync(mongo);
    await DbSeeder.SeedAdminAsync(mongo, app.Configuration);
}

if (app.Environment.IsDevelopment())
{
    app.UseCors("DevFileOrigin");
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Serve the static frontend (index.html) from the repo root so it's same-origin
// with /api in every environment — Render runs this as a single Web Service,
// so there's no separate static-file host and no CORS/cookie cross-origin concerns.
var repoRoot = Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, "..", ".."));
var fileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(repoRoot);
app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = fileProvider });
app.UseStaticFiles(new StaticFileOptions { FileProvider = fileProvider });

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
