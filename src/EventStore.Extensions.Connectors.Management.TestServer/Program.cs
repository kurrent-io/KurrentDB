using EventStore.Connect;
using EventStore.Connectors.Management;
using EventStore.Connectors.System;

Host.CreateDefaultBuilder(args)
    .ConfigureWebHostDefaults(webBuilder => webBuilder
        .ConfigureServices(services => {
            services
                .AddNodeSystemInfoProvider()
                .AddSurgeSystemComponents()
                .AddSurgeDataProtection(null!)
                .AddConnectorsManagementPlane();
        })
        .Configure(app => app.UseConnectorsManagementPlane()))
    .Build()
    .Run();

public partial class Program { }
