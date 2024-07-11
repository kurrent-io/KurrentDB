using EventStore.Connect.Connectors;
using EventStore.Connectors.Eventuous;
using EventStore.Streaming.Schema;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;

namespace EventStore.Connectors.Management;

public static class ManagementPlaneWireUp {
    public static void AddConnectorsManagementPlane(this IServiceCollection services, SchemaRegistry schemaRegistry) {
        services
            .AddGrpcSwagger()
            .AddSwaggerGen(x => x.SwaggerDoc(
                "v1",
                new OpenApiInfo {
                    Version     = "v1",
                    Title       = "Connectors Management API",
                    Description = "The API for managing connectors in EventStore"
                }
            ));

        services.AddAggregateStore<SystemEventStore>();

        services.AddCommandService<ConnectorApplication, ConnectorEntity>();

        // // TODO JC: What do we want to do with IAuthorizationProvider?
        // services.AddSingleton<IAuthorizationProvider, PassthroughAuthorizationProvider>();

        services.AddSingleton<ConnectorDomainServices.ValidateConnectorSettings>(
            settings => ConnectorsValidation.System.ValidateConfiguration(settings)
        );

        // lol I know, just you wait.
        schemaRegistry.RegisterSystemMessage<Contracts.Events.ConnectorCreated>().AsTask().GetAwaiter().GetResult();
        schemaRegistry.RegisterSystemMessage<Contracts.Events.ConnectorActivating>().AsTask().GetAwaiter().GetResult();
        schemaRegistry.RegisterSystemMessage<Contracts.Events.ConnectorRunning>().AsTask().GetAwaiter().GetResult();
        schemaRegistry.RegisterSystemMessage<Contracts.Events.ConnectorDeactivating>().AsTask().GetAwaiter().GetResult();
        schemaRegistry.RegisterSystemMessage<Contracts.Events.ConnectorStopped>().AsTask().GetAwaiter().GetResult();
        schemaRegistry.RegisterSystemMessage<Contracts.Events.ConnectorFailed>().AsTask().GetAwaiter().GetResult();
        schemaRegistry.RegisterSystemMessage<Contracts.Events.ConnectorRenamed>().AsTask().GetAwaiter().GetResult();
        schemaRegistry.RegisterSystemMessage<Contracts.Events.ConnectorReconfigured>().AsTask().GetAwaiter().GetResult();
        schemaRegistry.RegisterSystemMessage<Contracts.Events.ConnectorReset>().AsTask().GetAwaiter().GetResult();
        schemaRegistry.RegisterSystemMessage<Contracts.Events.ConnectorDeleted>().AsTask().GetAwaiter().GetResult();
    }

    public static void UseConnectorsManagementPlane(this IApplicationBuilder app) {
        app.UseSwagger();
        app.UseSwaggerUI(x => x.SwaggerEndpoint("/swagger/v1/swagger.json", "Connectors Management API v1"));

        app.UseRouting();
        app.UseEndpoints(endpoints => endpoints.MapGrpcService<ConnectorService>());
    }
}