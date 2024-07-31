using EventStore.Connect.Connectors;
using EventStore.Connect.Schema;
using EventStore.Connectors.Eventuous;
using EventStore.Connectors.Management.Reactors;
using EventStore.Streaming;
using EventStore.Streaming.Schema;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using static EventStore.Connectors.ConnectorsFeatureConventions.Messages;

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
            .AddAggregateStore<SystemEventStore>()
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
        services.AddSchemaRegistryStartupTask(
            "Connectors Management Schema Registration",
            static async (registry, ct) => {
                Type[] messageTypes = [
                    typeof(Contracts.Events.ConnectorCreated),
                    typeof(Contracts.Events.ConnectorActivating),
                    typeof(Contracts.Events.ConnectorRunning),
                    typeof(Contracts.Events.ConnectorDeactivating),
                    typeof(Contracts.Events.ConnectorStopped),
                    typeof(Contracts.Events.ConnectorFailed),
                    typeof(Contracts.Events.ConnectorRenamed),
                    typeof(Contracts.Events.ConnectorReconfigured),
                    typeof(Contracts.Events.ConnectorReset),
                    typeof(Contracts.Events.ConnectorDeleted),
                    typeof(Contracts.Events.ConnectorPositionCommitted)
                ];

                await messageTypes
                    .Select(async messageType => {
                        SchemaInfo schemaInfo = new(GetManagementMessageSubject(messageType.Name), SchemaDefinitionType.Json);
                        await registry.RegisterSchema(schemaInfo, "", messageType, ct).AsTask();
                    })
                    .WhenAll();
            }
        );

    // public static ISchemaRegistry RegisterConnectorsManagementMessages(this ISchemaRegistry registry) {
    //     Type[] messageTypes = [
    //         typeof(Contracts.Events.ConnectorCreated),
    //         typeof(Contracts.Events.ConnectorActivating),
    //         typeof(Contracts.Events.ConnectorRunning),
    //         typeof(Contracts.Events.ConnectorDeactivating),
    //         typeof(Contracts.Events.ConnectorStopped),
    //         typeof(Contracts.Events.ConnectorFailed),
    //         typeof(Contracts.Events.ConnectorRenamed),
    //         typeof(Contracts.Events.ConnectorReconfigured),
    //         typeof(Contracts.Events.ConnectorReset),
    //         typeof(Contracts.Events.ConnectorDeleted),
    //         typeof(Contracts.Events.ConnectorPositionCommitted)
    //     ];
    //
    //     messageTypes
    //         .Select(async messageType => {
    //             SchemaInfo schemaInfo = new(GetManagementMessageSubject(messageType.Name), SchemaDefinitionType.Json);
    //             await registry.RegisterSchema(schemaInfo, "", messageType, CancellationToken.None).AsTask();
    //         })
    //         .WhenAll().GetAwaiter().GetResult();
    //
    //     return registry;
    // }
}