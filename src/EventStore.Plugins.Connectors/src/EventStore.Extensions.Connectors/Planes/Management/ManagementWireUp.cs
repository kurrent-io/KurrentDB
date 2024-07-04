using EventStore.Connect.Connectors;
using EventStore.Connectors.Eventuous;
using EventStore.Core.Bus;
using EventStore.Streaming.Producers;
using EventStore.Streaming.Readers;
using Eventuous;
using FluentValidation.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;

namespace EventStore.Connectors.Management;

public static class ManagementWireUp {
    public static void AddConnectorsManagement(this IServiceCollection services) {
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

        services.AddKeyedSingleton<SystemReader>(
            "rdr-cnx-eventuous", (ctx, key) => SystemReader.Builder
                .ReaderId((string)key)
                .Publisher(ctx.GetRequiredService<IPublisher>())
                .LoggerFactory(ctx.GetRequiredService<ILoggerFactory>())
                .EnableLogging()
                .Create()
        );

        services.AddKeyedSingleton<SystemProducer>(
            "prd-cnx-eventuous", (ctx, key) => SystemProducer.Builder
                .ProducerId((string)key)
                .Publisher(ctx.GetRequiredService<IPublisher>())
                .LoggerFactory(ctx.GetRequiredService<ILoggerFactory>())
                .EnableLogging()
                .Create()
        );

        services.AddAggregateStore<SystemEventStore>(ctx => new SystemEventStore(
            ctx.GetRequiredKeyedService<SystemReader>("rdr-cnx-eventuous"),
            ctx.GetRequiredKeyedService<SystemProducer>("prd-cnx-eventuous"),
            ctx.GetRequiredService<ILoggerFactory>()
        ));

        services.AddCommandService<ConnectorApplication, ConnectorEntity>();

        // // TODO JC: What do we want to do with IAuthorizationProvider?
        // services.AddSingleton<IAuthorizationProvider, PassthroughAuthorizationProvider>();

        services.AddSingleton<ConnectorDomainServices.ValidateConnectorSettings>(settings => {
            try {
                return new ConnectorsValidation().ValidateConfiguration(settings.ToDictionary());
            } catch (InvalidConnectorTypeName ex) {
                // TODO JC: Should be done in the Connect Core.
                return new([new ValidationFailure("ConnectorTypeName", ex.Message)]);
            }
        });

        RegisterEventuousEvent<Contracts.Events.ConnectorCreated>();
        RegisterEventuousEvent<Contracts.Events.ConnectorActivating>();
        RegisterEventuousEvent<Contracts.Events.ConnectorRunning>();
        RegisterEventuousEvent<Contracts.Events.ConnectorDeactivating>();
        RegisterEventuousEvent<Contracts.Events.ConnectorStopped>();
        RegisterEventuousEvent<Contracts.Events.ConnectorReconfigured>();
        RegisterEventuousEvent<Contracts.Events.ConnectorFailed>();
        RegisterEventuousEvent<Contracts.Events.ConnectorDeleted>();

        return;

        static void RegisterEventuousEvent<T>() {
            if (!TypeMap.Instance.IsTypeRegistered<T>())
                TypeMap.Instance.AddType<T>(typeof(T).FullName!);
        }
    }

    public static void UseConnectorsManagement(this IApplicationBuilder app) {
        app.UseSwagger();
        app.UseSwaggerUI(x => x.SwaggerEndpoint("/swagger/v1/swagger.json", "Connectors Management API v1"));

        app.UseRouting();
        app.UseEndpoints(endpoints => endpoints.MapGrpcService<ConnectorService>());
    }
}