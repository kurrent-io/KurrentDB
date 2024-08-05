using EventStore.Connect.Connectors;
using EventStore.Connect.Producers.Configuration;
using EventStore.Connect.Readers.Configuration;
using EventStore.Connect.Schema;
using EventStore.Connectors.Eventuous;
using EventStore.Connectors.Management.Contracts.Events;
using EventStore.Connectors.Management.Reactors;
using EventStore.Streaming;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using static EventStore.Connectors.ConnectorsFeatureConventions;

namespace EventStore.Connectors.Management;

public static class ManagementPlaneWireUp {
    public static IServiceCollection AddConnectorsManagementPlane(this IServiceCollection services) {
        services.AddMessageSchemaRegistration();

        services
            .AddGrpc(x => x.EnableDetailedErrors = true)
            .AddJsonTranscoding();

        services.AddSingleton<ConnectorDomainServices.ValidateConnectorSettings>(ctx => {
            var validation = ctx.GetService<IConnectorValidator>() ?? new SystemConnectorsValidation();
            return validation.ValidateSettings;
        });

        services
            .AddEventStore<SystemEventStore>(ctx => {
                var reader   = ctx.GetRequiredService<Func<SystemReaderBuilder>>()()
                    .ReaderId("rdx-eventuous-eventstore")
                    .Create();

                var producer = ctx.GetRequiredService<Func<SystemProducerBuilder>>()()
                    .ProducerId("pdx-eventuous-eventstore")
                    .Create();

                return new SystemEventStore(reader, producer);
            })
            .AddCommandService<ConnectorsApplication, ConnectorEntity>();

        return services
            .AddConnectorsLifecycleReactor()
            .AddConnectorsStreamSupervisor();
    }

    public static void UseConnectorsManagementPlane(this IApplicationBuilder application) {
        application
            .UseRouting()
            .UseEndpoints(endpoints => endpoints.MapGrpcService<ConnectorsService>());
    }

    static IServiceCollection AddMessageSchemaRegistration(this IServiceCollection services) =>
        services.AddSchemaRegistryStartupTask("Connectors Management Schema Registration", static async (registry, token) => {
            Task[] tasks = [
                RegisterManagementMessages<ConnectorCreated>(registry, token),
                RegisterManagementMessages<ConnectorActivating>(registry, token),
                RegisterManagementMessages<ConnectorRunning>(registry, token),
                RegisterManagementMessages<ConnectorDeactivating>(registry, token),
                RegisterManagementMessages<ConnectorStopped>(registry, token),
                RegisterManagementMessages<ConnectorFailed>(registry, token),
                RegisterManagementMessages<ConnectorRenamed>(registry, token),
                RegisterManagementMessages<ConnectorReconfigured>(registry, token),
                RegisterManagementMessages<ConnectorDeleted>(registry, token)
            ];

            await tasks.WhenAll();
        });
}