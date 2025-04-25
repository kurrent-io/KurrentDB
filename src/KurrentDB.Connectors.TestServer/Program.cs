using KurrentDB.Connect;
using KurrentDB.Connectors.Infrastructure.System.Node.NodeSystemInfo;
using KurrentDB.Connectors.Planes.Management;

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
