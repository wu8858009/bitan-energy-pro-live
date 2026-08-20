namespace BiTanEnergyApi.Services;

public static class UploadsPathResolver
{
    public static string Resolve(IWebHostEnvironment env, IConfiguration config)
    {
        var configured = config["Uploads:RootPath"];
        var root = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(env.ContentRootPath, "App_Data", "uploads")
            : configured;
        Directory.CreateDirectory(root);
        return root;
    }
}
