using EventStore.Connectors;
using EventStore.Connectors.Control;
using EventStore.Connectors.Management;
using EventStore.Streaming.Schema;
using EventStore.Streaming.Schema.Serializers;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace EventStore.Plugins.Connectors;

[UsedImplicitly]
public class ConnectorsPlugin : TinyAppPlugin {
    protected override WebApplication BuildApp(TinyAppBuildContext ctx) {
        ctx.AppBuilder.Services.AddConnectorsManagementPlane(SchemaRegistry.Global);
        ctx.AppBuilder.Services.AddConnectorsControlPlane(SchemaRegistry.Global);

        ctx.AppBuilder.Services.AddSingleton(SchemaRegistry.Global);
        ctx.AppBuilder.Services.AddSingleton<ISchemaRegistry>(SchemaRegistry.Global);
        ctx.AppBuilder.Services.AddSingleton<ISchemaSerializer>(SchemaRegistry.Global);

        var app = ctx.AppBuilder.Build();

        app.UseConnectorsManagementPlane();

        return app;
    }
}