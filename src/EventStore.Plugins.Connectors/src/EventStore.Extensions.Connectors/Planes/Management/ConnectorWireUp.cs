using EventStore.Connect.Connectors;
using EventStore.Connectors.Eventuous;
using Eventuous;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace EventStore.Connectors.Management;

public static class ConnectorWireUp {
    public static void AddConnectorsManagement(this IServiceCollection services) {
        services
            .AddGrpc()
            .AddJsonTranscoding();

        services.AddAggregateStore<SystemEventStore>();

        services.AddFunctionalService<ConnectorApplication, ConnectorEntity>();

        services.AddSingleton<ConnectorDomainServices.ValidateConnectorSettings>(
            settings => {
                var validation = new ConnectorsValidation();
                return validation.ValidateConfiguration(settings);
            }
        );

        RegisterEventuousEvent<Contracts.Events.ConnectorCreated>();
        RegisterEventuousEvent<Contracts.Events.ConnectorActivating>();
        RegisterEventuousEvent<Contracts.Events.ConnectorRunning>();
        RegisterEventuousEvent<Contracts.Events.ConnectorDeactivating>();
        RegisterEventuousEvent<Contracts.Events.ConnectorStopped>();
        RegisterEventuousEvent<Contracts.Events.ConnectorReconfigured>();
        RegisterEventuousEvent<Contracts.Events.ConnectorFailed>();
        RegisterEventuousEvent<Contracts.Events.ConnectorDeleted>();

        return;

        static void RegisterEventuousEvent<T>() => TypeMap.Instance.AddType<T>(typeof(T).FullName!);
    }

    public static void UseConnectorsManagement(this IApplicationBuilder app) {
        app.UseEndpoints(x => x.MapGrpcService<ConnectorService>());
    }
}