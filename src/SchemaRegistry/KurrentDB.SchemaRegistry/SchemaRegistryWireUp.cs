using Kurrent.Surge;
using Kurrent.Surge.DuckDB;
using Kurrent.Surge.DuckDB.Projectors;
using Kurrent.Surge.Producers.Configuration;
using Kurrent.Surge.Readers.Configuration;
using Kurrent.Surge.Schema;
using Kurrent.Surge.Schema.Serializers;
using Kurrent.Surge.Schema.Validation;
using KurrentDB.SchemaRegistry.Infrastructure;
using KurrentDB.SchemaRegistry.Infrastructure.Grpc;
using KurrentDB.SchemaRegistry.Data;
using KurrentDB.SchemaRegistry.Domain;
using KurrentDB.SchemaRegistry.Infrastructure.Eventuous;
using KurrentDB.SchemaRegistry.Protocol.Schemas.Events;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using static KurrentDB.SchemaRegistry.SchemaRegistryConventions;

namespace KurrentDB.SchemaRegistry;

public static class SchemaRegistryWireUp {
    public static IServiceCollection AddSchemaRegistryService(this IServiceCollection services) {
        services.TryAddSingleton(TimeProvider.System);

        services.AddSingleton<GetUtcNow>(ctx => ctx.GetRequiredService<TimeProvider>().GetUtcNow);

        services.AddGrpcWithJsonTranscoding();
        services.AddGrpcRequestValidation();

        services.AddSingleton<ISchemaCompatibilityManager>(new NJsonSchemaCompatibilityManager());

        services.AddDuckDB()
            .WithKeyedConnectionProvider("schema-registry", $"DataSource=schema-registry-{Identifiers.GenerateShortId()}.ddb");

        services.AddMessageRegistration();
        services.AddCommandPlane();
        services.AddQueryPlane();

        return services
            .AddSingleton(Kurrent.Surge.Schema.SchemaRegistry.Global)
            .AddSingleton<ISchemaRegistry>(ctx => ctx.GetRequiredService<Kurrent.Surge.Schema.SchemaRegistry>())
            .AddSingleton<ISchemaSerializer>(ctx => ctx.GetRequiredService<Kurrent.Surge.Schema.SchemaRegistry>());
    }

    static IServiceCollection AddCommandPlane(this IServiceCollection services) {
        services.AddEventStore<SurgeEventStore>(
            ctx => {
                var reader = ctx.GetRequiredService<IReaderBuilder>()
                    .ReaderId("SurgeEventStoreReader")
                    .Create();

                var producer = ctx.GetRequiredService<IProducerBuilder>()
                    .ProducerId("SurgeEventStoreProducer")
                    .Create();

                var manager = ctx.GetRequiredService<IManagerBuilder>()
                    .ManagerId("SurgeEventStoreManager")
                    .Create();

                return new SurgeEventStore(reader, producer, manager);
            }
        );

        // Domain services

        // TODO: Correctly implement this
        services.AddSingleton<CheckAccess>(_ => ValueTask.FromResult(true));

        services.AddSingleton<LookupSchemaNameByVersionId>(ctx => {
            var queries = ctx.GetRequiredService<SchemaQueries>();
            return async (schemaVersionId, ct) => {
                var response = await queries.LookupSchemaName(new() { SchemaVersionId = schemaVersionId }, ct);
                return response.SchemaName;
            };
        });

        services.AddCommandService<SchemaApplication, SchemaEntity>();

        return services;
    }

    static IServiceCollection AddQueryPlane(this IServiceCollection services) {
        services.AddDuckDBProjection<SchemaProjections>(ctx => {
            var connectionProvider = ctx.GetRequiredKeyedService<DuckDBConnectionProvider>("schema-registry");
            return new DuckDBProjectorOptions(connectionProvider) {
                ConnectionProvider = connectionProvider,
                Filter             = SchemaRegistryConventions.Filters.SchemasFilter,
                InitialPosition    = SubscriptionInitialPosition.Latest,
                AutoCommit = new() {
                    Interval         = TimeSpan.FromSeconds(5),
                    RecordsThreshold = 500
                }
            };
        });

        services.AddSingleton<SchemaQueries>(ctx => {
            var connectionProvider   = ctx.GetRequiredKeyedService<DuckDBConnectionProvider>("schema-registry");
            var compatibilityManager = ctx.GetRequiredService<ISchemaCompatibilityManager>();
            return new SchemaQueries(connectionProvider, compatibilityManager);
        });

        return services;
    }

    static IServiceCollection AddMessageRegistration(this IServiceCollection services) {
        return services.AddSchemaMessageRegistrationStartupTask(
            "Schema Registry Message Registration",
            RegisterManagementMessages
        );

        static async Task RegisterManagementMessages(ISchemaRegistry registry, CancellationToken ct) {
            Task[] tasks = [
                RegisterMessages<SchemaCreated>(registry, ct),
                RegisterMessages<SchemaTagsUpdated>(registry, ct),
                RegisterMessages<SchemaDescriptionUpdated>(registry, ct),
                RegisterMessages<SchemaCompatibilityModeChanged>(registry, ct),
                RegisterMessages<SchemaVersionRegistered>(registry, ct),
                RegisterMessages<SchemaVersionsDeleted>(registry, ct),
                RegisterMessages<SchemaDeleted>(registry, ct),
            ];

            await tasks.WhenAll();
        }
    }

    public static WebApplication UseSchemaRegistryService(this WebApplication app) {
        app.MapGrpcService<SchemaRegistryService>();

        return app;
    }
}