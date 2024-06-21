using EventStore.Connectors.Eventuous;
using Eventuous;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace EventStore.Connectors.Management;

public static class ConnectorWireUp {
    public static void AddConnectorsManagement(this IServiceCollection services) {
        services.AddAggregateStore<SystemEventStore>();

        services.AddFunctionalService<ConnectorApplication, ConnectorEntity>();

        services.AddSingleton<ConnectorDomainServices.ValidateConnectorSettings>(
            (type, settings, ct) => {
                // need to validate settings upfront. how?
                // the connector assemblies must be loaded by now for this to work
                // must use somesort of master validator that will try to validate the settings
                // by instanciating some sort of validator


                return Task.FromResult((new Dictionary<string, string>(), true));
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