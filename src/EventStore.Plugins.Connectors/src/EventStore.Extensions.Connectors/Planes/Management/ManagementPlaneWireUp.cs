// ReSharper disable InconsistentNaming

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using EventStore.Connect.Connectors;
using EventStore.Connect.Producers.Configuration;
using EventStore.Connect.Readers.Configuration;
using EventStore.Connect.Schema;
using EventStore.Connectors.Eventuous;
using EventStore.Connectors.Infrastructure;
using EventStore.Connectors.Management.Contracts.Events;
using EventStore.Connectors.Management.Contracts.Queries;
using EventStore.Connectors.Management.Data;
using EventStore.Connectors.Management.Projectors;
using EventStore.Connectors.Management.Queries;
using EventStore.Connectors.Management.Reactors;
using EventStore.Connectors.System;
using EventStore.Plugins.Licensing;
using EventStore.Toolkit;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Grpc.JsonTranscoding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using static EventStore.Connectors.ConnectorsFeatureConventions;
using static EventStore.Connectors.Management.Queries.ConnectorQueryConventions;

namespace EventStore.Connectors.Management;

public static class ManagementPlaneWireUp {
    public static IServiceCollection AddConnectorsManagementPlane(this IServiceCollection services) {
        services.AddSingleton(ctx => new ConnectorsLicenseService(
            ctx.GetRequiredService<ILicenseService>(),
            ctx.GetRequiredService<ILogger<ConnectorsLicenseService>>()
        ));

        services.AddSingleton<ISnapshotProjectionsStore, SystemSnapshotProjectionsStore>();

        services.AddConnectorsManagementSchemaRegistration();

        services
            .AddGrpc(x => x.EnableDetailedErrors = true)
            .AddJsonTranscoding();

        services.PostConfigure<GrpcJsonTranscodingOptions>(options => {
            // https://github.com/dotnet/aspnetcore/issues/50401
            // TODO: Refactor into an extension method
            string[] props = ["UnarySerializerOptions", "ServerStreamingSerializerOptions"];

            foreach (var name in props) {
                var prop = options.GetType().GetProperty(name, BindingFlags.NonPublic | BindingFlags.Instance);

                if (prop?.GetValue(options) is not JsonSerializerOptions serializerOptions) continue;

                serializerOptions.PropertyNamingPolicy        = JsonNamingPolicy.CamelCase;
                serializerOptions.DictionaryKeyPolicy         = JsonNamingPolicy.CamelCase;
                serializerOptions.PropertyNameCaseInsensitive = true;
                serializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
                serializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            }
        });

        services
            .AddValidatorsFromAssembly(Assembly.GetExecutingAssembly())
            .AddSingleton<RequestValidationService>();

        // Commands
        services.AddSingleton<ConnectorDomainServices.ValidateConnectorSettings>(ctx => {
            var validation = ctx.GetService<IConnectorValidator>()
                          ?? new SystemConnectorsValidation();

            return validation.ValidateSettings;
        });

        services
            .AddEventStore<SystemEventStore>(ctx => {
                var reader = ctx.GetRequiredService<Func<SystemReaderBuilder>>()()
                    .ReaderId("EventuousReader")
                    .Create();

                var producer = ctx.GetRequiredService<Func<SystemProducerBuilder>>()()
                    .ProducerId("EventuousProducer")
                    .Create();

                return new SystemEventStore(reader, producer);
            })
            .AddCommandService<ConnectorsCommandApplication, ConnectorEntity>();

        // Queries
        services.AddSingleton<ConnectorQueries>(ctx => new ConnectorQueries(
            ctx.GetRequiredService<Func<SystemReaderBuilder>>(),
            ConnectorQueryConventions.Streams.ConnectorsStateProjectionStream)
        );

        services
            .AddConnectorsLifecycleReactor()
            .AddConnectorsStreamSupervisor()
            .AddConnectorsStateProjection();

        services.AddSystemStartupTask<ConfigureConnectorsManagementStreams>();

        return services;
    }

    public static void UseConnectorsManagementPlane(this IApplicationBuilder application) {
        application
            .UseEndpoints(endpoints => endpoints.MapGrpcService<ConnectorsCommandService>())
            .UseEndpoints(endpoints => endpoints.MapGrpcService<ConnectorsQueryService>());
    }

    internal static IServiceCollection AddConnectorsManagementSchemaRegistration(this IServiceCollection services) =>
        services.AddSchemaRegistryStartupTask("Connectors Management Schema Registration",
            static async (registry, token) => {
                Task[] tasks = [
                    RegisterManagementMessages<ConnectorCreated>(registry, token),
                    RegisterManagementMessages<ConnectorActivating>(registry, token),
                    RegisterManagementMessages<ConnectorRunning>(registry, token),
                    RegisterManagementMessages<ConnectorDeactivating>(registry, token),
                    RegisterManagementMessages<ConnectorStopped>(registry, token),
                    RegisterManagementMessages<ConnectorFailed>(registry, token),
                    RegisterManagementMessages<ConnectorRenamed>(registry, token),
                    RegisterManagementMessages<ConnectorReconfigured>(registry, token),
                    RegisterManagementMessages<ConnectorDeleted>(registry, token),
                    RegisterQueryMessages<ConnectorsSnapshot>(registry, token)
                ];

                await tasks.WhenAll();
            });
}