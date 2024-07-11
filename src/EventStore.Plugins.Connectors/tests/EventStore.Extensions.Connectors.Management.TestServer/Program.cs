using EventStore.Connectors.Management;
using EventStore.Streaming.Schema;

Host.CreateDefaultBuilder(args)
    .ConfigureWebHostDefaults(
        webBuilder => webBuilder
            .ConfigureServices(services => services.AddConnectorsManagementPlane(SchemaRegistry.Global))
            .Configure(app => app.UseConnectorsManagementPlane())
    )
    .Build()
    .Run();

public partial class Program { }