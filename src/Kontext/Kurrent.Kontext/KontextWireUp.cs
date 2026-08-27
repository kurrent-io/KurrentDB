using Kurrent.Kontext.Configuration;
using Kurrent.Kontext.Data;
using Kurrent.Kontext.Embeddings;
using Kurrent.Kontext.Entities;
using Kurrent.Kontext.Mcp;
using Kurrent.Kontext.Memory;
using Kurrent.Kontext.Memory.Mcp;
using Kurrent.Kontext.Records;
using Kurrent.Kontext.Records.Mcp;
using KurrentDB.Core;
using KurrentDB.Core.Hosting;
using KurrentDB.Core.Hosting.Experimental;
using KurrentDB.Core.Settings;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Kurrent.Kontext;

public static class KontextWireUp {
    extension(IServiceCollection services) {
        public IServiceCollection AddKontext(IConfiguration configuration) {
            var options = services.AddKontextOptions(configuration);
            
            return services
                .AddSystemReadiness()
                .AddSystemStartupManager()
                .AddKontextStorage()
                .AddKontextEmbeddings(options.Embeddings)
                .AddKontextMemory()
                .AddKontextEntities()
                .AddKontextRecords()
                .AddKontextMcp();
        }

        /// <summary>
        /// The one MCP server, carrying every module's tools. It lives here rather than in a module
        /// because <c>AddMcpServer</c> registers the server itself and returns the builder the tools
        /// attach to — so one call has to cover all of them.
        /// </summary>
        IServiceCollection AddKontextMcp() {
            services.AddHttpContextAccessor();
            services.TryAddSingleton<McpMemoryService>();
            services.TryAddSingleton<McpRecordsService>();

            services
                .AddMcpServer(opts => opts.ServerInstructions = McpInstructions.Server)
                .WithToolsFromResources<McpMemoryService>()
                .WithToolsFromResources<McpRecordsService>()
                .WithHttpTransport();

            return services;
        }

        KontextOptions AddKontextOptions(IConfiguration configuration) {
            var options = configuration.GetSection(KontextOptions.SectionName).Get<KontextOptions>() ?? new();
            services.AddSingleton(options);
            services.TryAddSingleton<KontextMemoryOptions>();
            return options;
        }

        IServiceCollection AddSystemStartupManager() {
            // TODO SS: need to increase the timeout from 30 seconds to something configurable, or at least 5 minutes.
            // The default is too short for the migrations to complete on a cold start.
            
            services.TryAddSingleton<SystemStartupManager>();
            
            // IHostedService is a multi-registration service type: TryAdd would find another hosted
            // service and skip. AddHostedService(factory) uses TryAddEnumerable, which dedupes on the
            // implementation type instead.
            services.AddHostedService(sp => sp.GetRequiredService<SystemStartupManager>());

            // Factory, not TryAddSingleton<TService, TImpl>: the latter builds a second instance whose
            // completion source nothing ever signals.
            services.TryAddSingleton<IStartupWorkCompletionMonitor>(sp => sp.GetRequiredService<SystemStartupManager>());

            return services;
        }
        
        IServiceCollection AddKontextStorage() {
            services.AddSingleton<KontextDataSource>(sp => {
                var database    = sp.GetRequiredService<ClusterVNodeOptions>().Database;
                var indexPath   = database.Index ?? Path.Combine(database.Db, ESConsts.DefaultIndexDirectoryName);
                var storagePath = Path.Combine(indexPath, "kontext");

                // The filename must match DuckDBConnectionPoolLifetime's own composition of the
                // node's database path, or the read-only attach points at nothing.
                var sharedDatabasePath = Path.Combine(database.Db, "kurrent.ddb");

                return new(storagePath, $"{storagePath}.tmp", sharedDatabasePath);
            });
            
            services.AddSystemStartupTask("Kontext Migrations", static async (node, sp, ct) => {
                var dataSource = sp.GetRequiredService<KontextDataSource>();
                var logger     = sp.GetRequiredService<ILoggerFactory>().CreateLogger("KontextMigrations");
                var migrations = KontextMigrations.CreateEngine(dataSource, logger);
                await migrations.EnsureAsync(ct);
            });
            
            return services;
        }

        IServiceCollection AddKontextEmbeddings(KontextEmbeddingsOptions options) {
            services.AddKontextEmbeddings(
                options.Provider,
                options.Local,
                options.OpenAI,
                options.Ollama,
                options.GoogleVertexAI,
                options.AmazonBedrock);

            return services;
        }
    }

    extension(IApplicationBuilder app) {
        public IApplicationBuilder UseKontext() {
            const string mcpBasePath = "/kontext/mcp";

            app.Use(async (context, next) => {
                if (context.Request.Path.StartsWithSegments(mcpBasePath) && context.User.Identity?.IsAuthenticated != true) {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }

                await next();
            });

            return app
                .UseRouting()
                .UseEndpoints(ep => ep.MapMcp(mcpBasePath))
                .UseKontextMemory()
                .UseKontextRecords();
        }
    }
}
