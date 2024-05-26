using EventStore.Connectors.ControlPlane;
using EventStore.Connectors.ControlPlane.Coordination;
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

public sealed class ConnectorsPlugin : SubsystemsPlugin {
    public override void ConfigureServices(IServiceCollection services, IConfiguration configuration) {
	    services.AddSingleton<IPublisher>(ctx => ctx.GetRequiredService<StandardComponents>().MainQueue);
	    services.AddSingleton<ISubscriber>(ctx => ctx.GetRequiredService<StandardComponents>().MainBus);
	    
	    services.AddSingleton<SystemConnectorsFactory>();
        
        services
            .AddGrpc()
            .AddJsonTranscoding();
        
        services.AddConnectorsControlPlane();
    }

    public override void ConfigureApplication(IApplicationBuilder app, IConfiguration configuration) =>
        app.UseConnectorsManagement();

    public override async Task Start() {
	    // because (unfortunately) the gossip listener service raises a schemaless event,
	    // a proper schema must be registered for it to be consumed by the control plane
	    await SchemaRegistry.Global.RegisterSchema<GossipUpdatedInMemory>(
		    GossipListenerService.EventType, SchemaDefinitionType.Json
		);
    }
}