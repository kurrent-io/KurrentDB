using FluentValidation;
using Kurrent.Kontext.Data;
using Kurrent.Kontext.Edges.Grpc;
using Kurrent.Kontext.Embeddings;
using Kurrent.Kontext.Infrastructure.Data;
using Kurrent.Kontext.Infrastructure.FluentValidation;
using Kurrent.Kontext.Infrastructure.Validation;
using Kurrent.Kontext.Mcp;
using Kurrent.Kontext.Modules.Entities;
using Kurrent.Kontext.Modules.Entities.Data;
using Kurrent.Kontext.Modules.Records;
using Kurrent.Kontext.Retrieval;
using Kurrent.Surge.Schema;
using KurrentDB.Core;
using KurrentDB.Core.Hosting;
using KurrentDB.Core.Settings;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using EmbeddingGenerator = Microsoft.Extensions.AI.IEmbeddingGenerator<string, Microsoft.Extensions.AI.Embedding<float>>;
using EntityContracts = Kurrent.Kontext.Contracts.V3.Entities;
using MemoryContracts = Kurrent.Kontext.Contracts.V3.Memory;

namespace Kurrent.Kontext.Modules.Memory;

public static class KontextMemoryWireUp {
    extension(IServiceCollection services) {
        #region ->> Composition Root <<-

        /// <summary>
        /// The whole system in one call: binds <see cref="KontextOptions"/> from
        /// <see cref="KontextOptions.SectionName"/> and chains the logical groups. Hosts wanting a
        /// partial composition call the groups directly.
        /// </summary>
        public IServiceCollection AddKontext(IConfiguration configuration) {
            var options = configuration.GetSection(KontextOptions.SectionName).Get<KontextOptions>() ?? new();

            return services
                .AddKontextOptions(options)
                .AddKontextStorage(options)
                .AddKontextEmbeddings(options.Embeddings)
                .AddKontextRetrieval()
                .AddKontextMemory()
                .AddKontextGrpcEdge()
                .AddKontextMcpEdge()
                .AddMessageRegistration()
                .AddKontextIndexing();
        }

        #endregion // Composition Root

        #region ->> Groups <<-

        public IServiceCollection AddKontextOptions(KontextOptions options) {
            services.AddSingleton(options);

            services.TryAddSingleton<KontextMemoryOptions>();

            return services;
        }

        /// <summary>
        /// The engine needs no file: everything durable lives in lance (tables AND checkpoints),
        /// so each connection opens an in-memory catalog and the lance ATTACH provides all shared
        /// state — pinned by <c>InMemoryEngineProbeTests</c>.
        /// </summary>
        public IServiceCollection AddKontextStorage(KontextOptions options) {
            services.AddSingleton(sp => {
                var database    = sp.GetRequiredService<ClusterVNodeOptions>().Database;
                var indexPath   = database.Index ?? Path.Combine(database.Db, ESConsts.DefaultIndexDirectoryName);
                var storagePath = Path.Combine(indexPath, "kontext");

                // The filename must match DuckDBConnectionPoolLifetime's own composition of the
                // node's database path, or the read-only attach points at nothing.
                var sharedDatabasePath = Path.Combine(database.Db, "kurrent.ddb");

                return new KontextDataSource(storagePath, $"{storagePath}.tmp", sharedDatabasePath);
            });

            return services;
        }

        public IServiceCollection AddKontextEmbeddings(KontextEmbeddingsOptions options) {
            services.AddKontextEmbeddings(
                options.Provider,
                options.Local,
                options.OpenAI,
                options.Ollama,
                options.GoogleVertexAI,
                options.AmazonBedrock);

            return services;
        }

        /// <summary>The memory service: the store, the domain workflows, and their validation surface.</summary>
        public IServiceCollection AddKontextMemory() {
            services.TryAddSingleton(sp => new KontextDataStore(
                sp.GetRequiredService<KontextDataSource>()));

            services.TryAddSingleton<KontextMemory>();

            services.TryAddSingleton<IKontextMemory>(sp => new KontextMemoryValidationDecorator(
                sp.GetRequiredService<KontextMemory>(),
                sp.GetRequiredService<RequestValidationService>()));

            return services.AddRequestValidation();
        }

        public IServiceCollection AddKontextGrpcEdge() {
            // Self-contained: the server host registers gRPC too, but this group must not
            // depend on it — AddGrpc is internally idempotent.
            services.AddGrpc();
            services.TryAddSingleton<GrpcMemoryService>();
            return services;
        }

        public IServiceCollection AddKontextMcpEdge() {
            // All agent-facing text — server instructions, tool and parameter descriptions, and model schema
            // descriptions — lives in McpInstructions.resx and is applied by WithToolsFromResources.
            services.AddHttpContextAccessor();
            services.TryAddSingleton<McpMemoryService>();

            services
                .AddMcpServer(options => options.ServerInstructions = McpInstructions.Server)
                .WithToolsFromResources<McpMemoryService>()
                .WithHttpTransport();

            return services;
        }

        /// <summary>
        /// The write side. The startup task is the readiness-gated gate the whole server uses
        /// (SystemStartupManager): the dimension probe fails fast — a mismatched model kills
        /// startup instead of poisoning the vector store, and the probe forces the model to
        /// load, so a broken model surfaces here too — then the migration stream runs: ONE
        /// bootstrap creates every table and eager index.
        /// </summary>
        public IServiceCollection AddKontextIndexing() {
            services.AddSystemStartupTask("Kontext Bootstrap", static async (_, sp, ct) => {
                await sp.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>()
                    .EnsureDimensionAsync(sp.GetRequiredService<KontextOptions>().Embeddings.Dimension, ct)
                    .ConfigureAwait(false);

                await new KontextSchemaBootstrap(
                        sp.GetRequiredService<KontextDataSource>(),
                        sp.GetRequiredService<ILoggerFactory>())
                    .EnsureAsync(ct)
                    .ConfigureAwait(false);
            });

            services.AddKontextMemoryProjector();
            services.AddKontextEntityProjector();
            services.AddKontextEntityResolution();
            services.AddKontextRecordsIndexer();

            return services;
        }

        IServiceCollection AddMessageRegistration() {
            return services.AddSystemStartupTask("Kontext Message Registration", static (_, sp, ct) =>
                RegisterKontextMessages(sp.GetRequiredService<ISchemaRegistry>(), ct));

            static async Task RegisterKontextMessages(ISchemaRegistry registry, CancellationToken ct) {
                Task[] tasks = [
                    KontextConventions.RegisterMessages<MemoryContracts.MemoriesRetained>(registry, KontextConventions.Streams.MemoriesStreamPrefix, ct),
                    KontextConventions.RegisterMessages<MemoryContracts.MemoryRetracted>(registry, KontextConventions.Streams.MemoriesStreamPrefix, ct),
                    KontextConventions.RegisterMessages<MemoryContracts.MemoriesRecalled>(registry, KontextConventions.Streams.MemoriesStreamPrefix, ct),
                    KontextConventions.RegisterMessages<MemoryContracts.MemoriesAccessed>(registry, KontextConventions.Streams.MemoriesStreamPrefix, ct),
                    KontextConventions.RegisterMessages<MemoryContracts.ReflectionCompleted>(registry, KontextConventions.Streams.MemoriesStreamPrefix, ct),
                    KontextConventions.RegisterMessages<EntityContracts.EntitiesMentioned>(registry, KontextConventions.Streams.EntitiesStreamPrefix, ct),
                    KontextConventions.RegisterMessages<EntityContracts.EntitiesMerged>(registry, KontextConventions.Streams.EntitiesStreamPrefix, ct),

                    // Surge's Checkpoint contract: type resolution on read is in-process, so
                    // without this a restarted node cannot decode its own checkpoint stream and
                    // silently reprocesses from Earliest.
                    KontextConventions.RegisterMessages<Kurrent.Surge.Protocol.Consumers.Checkpoint>(registry, KontextConventions.Streams.KontextStreamPrefix, ct),
                ];

                await Task.WhenAll(tasks);
            }
        }

        IServiceCollection AddRequestValidation() {
            services.TryAddSingleton<IValidator<MemoryContracts.RetainRequest>, RetainRequestValidator>();
            services.TryAddSingleton<IValidator<MemoryContracts.RetractRequest>, RetractRequestValidator>();
            services.TryAddSingleton<IValidator<MemoryContracts.RecallRequest>, RecallRequestValidator>();
            services.TryAddSingleton<IValidator<MemoryContracts.ReclaimRequest>, ReclaimRequestValidator>();
            services.TryAddSingleton<IValidator<MemoryContracts.RecollectRequest>, RecollectRequestValidator>();
            services.TryAddSingleton<IValidator<MemoryContracts.ReflectRequest>, ReflectRequestValidator>();
            services.TryAddSingleton<RequestValidationService>();
            return services;
        }

        #endregion // Groups
    }
}

public static class KontextApplicationBuilderExtensions {
    const string McpBasePath = "/kontext/mcp";

    extension(IApplicationBuilder app) {
        /// <summary>
        /// Maps both edges. Hosts wanting one of them call <see cref="UseKontextMcp"/> or
        /// <see cref="UseKontextGrpc"/> directly.
        /// </summary>
        public IApplicationBuilder UseKontext() => app.UseKontextMcp().UseKontextGrpc();

        /// <summary>
        /// Maps the MCP edge at <c>/kontext/mcp</c> behind an authenticated-user gate. A
        /// Kontext-owned authorization operation replaces the plain gate when the operations
        /// taxonomy lands; the workspace-era operations are gone with the workspaces.
        /// </summary>
        public IApplicationBuilder UseKontextMcp() {
            app.Use(async (context, next) => {
                if (context.Request.Path.StartsWithSegments(McpBasePath) && context.User.Identity?.IsAuthenticated != true) {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }

                await next();
            });

            // UseRouting before UseEndpoints, the SchemaRegistry precedent — without it the
            // pipeline build throws and takes the whole node down with it.
            app.UseRouting();

            return app.UseEndpoints(endpoints => endpoints.MapMcp(McpBasePath));
        }

        /// <summary>Maps the gRPC memory service.</summary>
        public IApplicationBuilder UseKontextGrpc() {
            app.UseRouting();

            return app.UseEndpoints(endpoints => endpoints.MapGrpcService<GrpcMemoryService>());
        }
    }
}


static class KontextRetrievalServiceCollectionExtensions {
    /// <param name="services">The service collection.</param>
    extension(IServiceCollection services) {
        public IServiceCollection AddKontextRetrieval() {
            // First-wins: a pre-registered IKontextRetriever (composed directly with
            // KontextRetriever.New()) beats the shipped Focused chain.
            services.TryAddSingleton<IKontextRetriever>(CreateDefaultRetriever);

            return services;
        }
    }

    static IKontextRetriever CreateDefaultRetriever(IServiceProvider sp) =>
        KontextRetriever.New()
            .Focused(
                sp.GetRequiredService<KontextDataStore>(),
                sp.GetRequiredService<EmbeddingGenerator>(),
                sp.GetService<TimeProvider>())
            .Build();
}
