using EventStore.Connectors.Management;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;

namespace EventStore.Extensions.Connectors.Management.TestServer;

public class TestManagementServerEntrypoint {
    public static void Main(string[] args) =>
        CreateHostBuilder(args).Build().Run();

    static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureWebHostDefaults(
                webBuilder => webBuilder
                    .ConfigureServices(services => services.AddConnectorsManagement())
                    .Configure(app => app.UseConnectorsManagement())
            );
}