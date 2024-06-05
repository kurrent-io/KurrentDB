using EventStore.Connectors.Control;
using EventStore.Connectors.Control.Coordination;
using EventStore.Connectors.Management;
using EventStore.Core;
using EventStore.Core.Bus;
using EventStore.Core.Services.Storage.InMemory;
using EventStore.Plugins;
using EventStore.Streaming.Connectors;
using EventStore.Streaming.Schema;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EventStore.Connectors;

public sealed class ConnectorsPlugin : Plugin {
    public override void ConfigureServices(IServiceCollection services, IConfiguration configuration) {
        // because (unfortunately) the gossip listener service raises a schemaless event,
        // a proper schema must be registered for it to be consumed by the control plane
        SchemaRegistry.Global.RegisterSchema<GossipUpdatedInMemory>(
            GossipListenerService.EventType, SchemaDefinitionType.Json
        ).AsTask().GetAwaiter().GetResult();

        services.AddSingleton(SchemaRegistry.Global);

        services.AddSingleton<IPublisher>(ctx => ctx.GetRequiredService<StandardComponents>().MainQueue);
	    services.AddSingleton<ISubscriber>(ctx => ctx.GetRequiredService<StandardComponents>().MainBus);

        services.AddSingleton<SystemClient>();

	    services.AddSingleton<SystemConnectorsFactory>();

        services
            .AddGrpc()
            .AddJsonTranscoding(x => x.JsonSettings.WriteIndented = true);

        services.AddConnectorsControlPlane();
    }

    public override void ConfigureApplication(IApplicationBuilder app, IConfiguration configuration) {
        app.UseConnectorsManagement();
    }
}