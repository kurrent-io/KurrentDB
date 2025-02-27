using EventStore.Connect;
using EventStore.Connectors.Infrastructure.Security;
using EventStore.Connectors.Management;
using EventStore.Connectors.System;

Host.CreateDefaultBuilder(args)
    .ConfigureWebHostDefaults(webBuilder => webBuilder
        .ConfigureServices(services => {
            services
                .AddNodeSystemInfoProvider()
                .AddConnectSystemComponents()
                .AddConnectorsManagementPlane()
                .AddConnectorsDataProtection();
        })
        .Configure(app => app.UseConnectorsManagementPlane()))
    .Build()
    .Run();

public partial class Program { }