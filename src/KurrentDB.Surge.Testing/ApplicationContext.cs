using Microsoft.Extensions.Configuration;

namespace KurrentDB.Surge.Testing;

public static class ApplicationContext {
    public static IConfiguration Configuration { get; private set; } = null!;

    public static void Initialize() {
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";

        Configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($"appsettings.{environment}.json", optional: true)                    // Accept default naming convention
            .AddJsonFile($"appsettings.{environment.ToLowerInvariant()}.json", optional: true) // Linux is case-sensitive
            .AddEnvironmentVariables()
            .Build();
    }
}
