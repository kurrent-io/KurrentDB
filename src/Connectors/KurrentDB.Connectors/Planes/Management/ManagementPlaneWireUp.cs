// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable InconsistentNaming

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using KurrentDB.Connect.Connectors;
using KurrentDB.Connect.Producers.Configuration;
using KurrentDB.Connect.Readers.Configuration;
using KurrentDB.Connect.Schema;
using KurrentDB.Connectors.Infrastructure;
using KurrentDB.Connectors.Management.Contracts.Events;
using KurrentDB.Connectors.Management.Contracts.Queries;
using EventStore.Plugins.Licensing;
using FluentValidation;
using Kurrent.Surge.Connectors;
using Kurrent.Surge.DataProtection;
using Kurrent.Surge;
using KurrentDB.Connectors.Infrastructure.Connect.Components.Connectors;
using KurrentDB.Connectors.Infrastructure.Eventuous;
using KurrentDB.Connectors.Infrastructure.System.Node;
using KurrentDB.Connectors.Management;
using KurrentDB.Connectors.Planes.Management.Data;
using KurrentDB.Connectors.Planes.Management.Domain;
using KurrentDB.Connectors.Planes.Management.Projectors;
using KurrentDB.Connectors.Planes.Management.Queries;
using KurrentDB.Core.Bus;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Grpc.JsonTranscoding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using static KurrentDB.Connectors.Planes.ConnectorsFeatureConventions;
using static KurrentDB.Connectors.Planes.Management.Queries.ConnectorQueryConventions;

namespace KurrentDB.Connectors.Planes.Management;

public static class ManagementPlaneWireUp {
    public static IServiceCollection AddConnectorsManagementPlane(this IServiceCollection services) {
        services.AddSingleton<IConnectorDataProtector, ConnectorsMasterDataProtector>();

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
                          ?? SystemConnectorsValidation.Instance;

            return validation.ValidateSettings;
        });

        services.AddSingleton<ConnectorDomainServices.ProtectConnectorSettings>(ctx => {
            var dataProtector = ctx.GetRequiredService<IConnectorDataProtector>();
            return dataProtector.Protect;
        });

        services.AddSingleton<ConnectorsStreamSupervisor>(ctx => {
            var options = new ConnectorsStreamSupervisorOptions {
                Leases      = new(MaxCount: 10),
                Checkpoints = new(MaxCount: 10)
            };

            return new ConnectorsStreamSupervisor(
                options,
                ctx.GetRequiredService<IPublisher>(),
                ctx.GetRequiredService<IDataProtector>(),
                ctx.GetRequiredService<ILogger<ConnectorsStreamSupervisor>>()
            );
        });

        services.AddSingleton<ConnectorDomainServices.ConfigureConnectorStreams>(ctx => {
            var supervisor = ctx.GetRequiredService<ConnectorsStreamSupervisor>();
            return supervisor.ConfigureConnectorStreams;
        });

        services.AddSingleton<ConnectorDomainServices.DeleteConnectorStreams>(ctx => {
            var supervisor = ctx.GetRequiredService<ConnectorsStreamSupervisor>();
            return supervisor.DeleteConnectorStreams;
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
            ctx.GetRequiredService<IConnectorDataProtector>(),
            ConnectorQueryConventions.Streams.ConnectorsStateProjectionStream
        ));

        services.AddConnectorsStateProjection();

        services.AddSystemStartupTask<ConfigureConnectorsManagementStreams>();

        return services;
    }

    public static void UseConnectorsManagementPlane(this IApplicationBuilder application) {
        application
            .UseEndpoints(endpoints => endpoints.MapGrpcService<ConnectorsCommandService>())
            .UseEndpoints(endpoints => endpoints.MapGrpcService<ConnectorsQueryService>());
    }

    static IServiceCollection AddConnectorsManagementSchemaRegistration(this IServiceCollection services) =>
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
