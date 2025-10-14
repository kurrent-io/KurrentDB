// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Surge.Schema;
using KurrentDB.Connect;
using KurrentDB.Connectors.Infrastructure.System.Node.NodeSystemInfo;
using KurrentDB.Connectors.Planes.Management;

Host.CreateDefaultBuilder(args)
    .ConfigureWebHostDefaults(webBuilder => webBuilder
        .ConfigureServices(services => {
            services
                .AddNodeSystemInfoProvider()
				.AddSurgeSchemaRegistry()
                .AddSurgeSystemComponents()
                .AddSurgeDataProtection(null!)
                .AddConnectorsManagementPlane();
        })
        .Configure(app => app.UseConnectorsManagementPlane()))
    .Build()
    .Run();

public partial class Program { }
