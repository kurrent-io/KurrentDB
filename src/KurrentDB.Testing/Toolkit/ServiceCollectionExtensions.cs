using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace KurrentDB.Testing;

public static class ServiceCollectionExtensions {
    // public static IServiceCollection AddTestLogging(this IServiceCollection services) =>
    //     services.AddLogging(logging => logging.AddProvider(ToolkitTestLoggerProvider.Instance));

    // public static IServiceCollection AddTestLogging(this IServiceCollection services) =>
    //     services.AddLogging(logging => logging.AddSerilog());

    public static IServiceCollection AddTestTimeProvider(this IServiceCollection services, DateTimeOffset? startDateTime = null) =>
        services
            .AddSingleton(new FakeTimeProvider(startDateTime ?? DateTimeOffset.UtcNow))
            .AddSingleton<TimeProvider>(sp => sp.GetRequiredService<FakeTimeProvider>());
}
