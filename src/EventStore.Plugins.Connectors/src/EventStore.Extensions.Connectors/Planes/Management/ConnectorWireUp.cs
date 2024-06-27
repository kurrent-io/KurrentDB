using EventStore.Connect.Connectors;
using EventStore.Connectors.Eventuous;
using EventStore.Core.Bus;
using EventStore.Streaming.Producers;
using EventStore.Streaming.Readers;
using Eventuous;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;

namespace EventStore.Connectors.Management;

public static class ConnectorWireUp {
    public static void AddConnectorsManagement(this IServiceCollection services) {
        services
            .AddGrpcSwagger()
            .AddSwaggerGen(
                c => c.SwaggerDoc(
                    "v1",
                    new OpenApiInfo {
                        Version     = "v1",
                        Title       = "Connectors Control Plane Management API",
                        Description = "The API for managing connectors in EventStore"
                    }
                )
            );

        services.AddSingleton<SystemProducer>(
            serviceProvider => {
                var publisher     = serviceProvider.GetRequiredService<IPublisher>();
                var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();

                var reader = SystemProducer.Builder
                    .ProducerId("connectors-producer")
                    .Publisher(publisher)
                    .LoggerFactory(loggerFactory)
                    .EnableLogging()
                    .Create();

                return reader;
            }
        );

        services.AddAggregateStore<SystemEventStore>();
        services.AddCommandService<ConnectorApplication, ConnectorEntity>();

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

    public static void UseConnectorsManagement(this WebApplication app) {
        app.UseSwagger();
        app.UseSwaggerUI(
            c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Connectors Management API v1")
        ); // TODO JC: Do we always want to expose the UI? It's common to only expose for development.

        app.MapGrpcService<ConnectorService>();
    }
}