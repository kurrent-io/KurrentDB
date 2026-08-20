using FluentValidation;
using Kurrent.Kontext.Infrastructure.Validation;
using Kurrent.Kontext.Mcp;
using Kurrent.Kontext.Memory.Data;
using Kurrent.Kontext.Memory.Grpc;
using Kurrent.Kontext.Memory.Mcp;
using Kurrent.Kontext.Retrieval;
using Kurrent.Surge;
using Kurrent.Surge.Producers;
using Kurrent.Surge.Producers.Configuration;
using Kurrent.Surge.Schema;
using KurrentDB.Core.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using EmbeddingGenerator = Microsoft.Extensions.AI.IEmbeddingGenerator<string, Microsoft.Extensions.AI.Embedding<float>>;

namespace Kurrent.Kontext.Memory;

public static class KontextMemoryWireUp {
    extension(IServiceCollection services) {
        /// <summary>The memory service: the store, the domain workflows, and their validation surface.</summary>
        public IServiceCollection AddKontextMemory() {
      
            // The wall clock as a dependency, so retain's timestamp is controllable in tests.
            // TryAdd so a host that already registered a clock keeps it.
            services.TryAddSingleton(TimeProvider.System);

            services.TryAddSingleton<KontextMemoryDataStore>();                                 // TODO SS: Rename to KontextMemoryStore
            services.TryAddSingleton<KontextMemory>();                                    // TODO SS: Rename to KontextMemoryService
            services.TryAddSingleton<IKontextMemory, KontextMemoryValidationDecorator>(); // TODO SS: Rename to KontextMemoryValidationService

            services.AddMessageRegistration();
            services.AddMemoryWritePath();

            services
                .AddRequestValidation()
                .AddGrpcEdge()
                .AddMcpEdge();

            services.AddKontextRetrieval();
            
            services.AddKontextMemoryProjector();
            
            return services;
        }

        IServiceCollection AddRequestValidation() {
            services.TryAddSingleton<RequestValidationService>();
            services.TryAddSingleton<IValidator<Contracts.RetainRequest>, RetainRequestValidator>();
            services.TryAddSingleton<IValidator<Contracts.RecallRequest>, RecallRequestValidator>();
            services.TryAddSingleton<IValidator<Contracts.ReclaimRequest>, ReclaimRequestValidator>();
            services.TryAddSingleton<IValidator<Contracts.RecollectRequest>, RecollectRequestValidator>();
            services.TryAddSingleton<IValidator<Contracts.ReflectRequest>, ReflectRequestValidator>();
            return services;
        }

        IServiceCollection AddGrpcEdge() {
            services.AddGrpc();
            services.TryAddSingleton<GrpcMemoryService>();
            return services;
        }

        IServiceCollection AddMcpEdge() {
            services.AddHttpContextAccessor();
            services.TryAddSingleton<McpMemoryService>();

            services
                .AddMcpServer(opts => opts.ServerInstructions = McpInstructions.Server)
                .WithToolsFromResources<McpMemoryService>()
                .WithHttpTransport();

            return services;
        }

        /// <summary>
        /// Binds <see cref="AppendEvent"/> to a Surge producer on the memories stream. Retain does
        /// not touch the read model: it appends here, and the projector carries the event into the
        /// lance table it owns.
        /// </summary>
        IServiceCollection AddMemoryWritePath() {
            services.TryAddSingleton<AppendEvent>(sp => {
                // One producer for the life of the process, captured by the delegate — the same
                // shape SchemaRegistry uses for its Eventuous producer.
                var producer = sp.GetRequiredService<IProducerBuilder>()
                    .ProducerId("KontextMemoryProducer")
                    .Create();

                return async (evt, ct) => {
                    var message = Message.Builder
                        .Value(evt)
                        .WithSchemaType(SchemaDataFormat.Json)
                        .Create();

                    var request = ProduceRequest.Builder
                        .Stream(KontextConventions.Streams.MemoriesStreamPrefix)
                        .Messages(message)
                        .Create();

                    var result = await producer.Produce(request);

                    // Surge reports the failure on the result rather than throwing, so an unchecked
                    // Produce would let retain return ids for an event that never landed.
                    if (result is { Success: false, Error: not null })
                        throw result.Error;

                    if (!result.Success)
                        throw new InvalidOperationException(
                            $"Failed to append {evt.GetType().Name} to {KontextConventions.Streams.MemoriesStreamPrefix}");
                };
            });

            return services;
        }

        IServiceCollection AddMessageRegistration() {
            return services.AddSystemStartupTask(
                "Kontext :: Message Registration",
                async static (_, sp, ct) => {
                    var registry = sp.GetRequiredService<ISchemaRegistry>();
                    await RegisterMemoryMessages(registry, ct);
                    await RegisterEntityMessages(registry, ct);
                });

            static async Task RegisterMemoryMessages(ISchemaRegistry registry, CancellationToken ct) {
                Task[] tasks = [
                    KontextConventions.RegisterMessages<Contracts.MemoriesRetained>(registry, ct),
                    KontextConventions.RegisterMessages<Contracts.MemoriesRecalled>(registry, ct),
                    KontextConventions.RegisterMessages<Contracts.MemoriesAccessed>(registry, ct),
                    KontextConventions.RegisterMessages<Contracts.ReflectionCompleted>(registry, ct),
                ];

                await Task.WhenAll(tasks);
            }

            static async Task RegisterEntityMessages(ISchemaRegistry registry, CancellationToken ct) {
                Task[] tasks = [];

                await Task.WhenAll(tasks);
            }
        }
        
        IServiceCollection AddKontextRetrieval() {
            services.TryAddSingleton<IKontextRetriever>(sp => KontextRetriever
                .New()
                .Focused(
                    sp.GetRequiredService<KontextMemoryDataStore>(),
                    sp.GetRequiredService<EmbeddingGenerator>(),
                    sp.GetService<TimeProvider>())
                .Build());

            return services;
        }
    }
    
    extension(IApplicationBuilder app) {
        public IApplicationBuilder UseKontextMemory() {
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
                .UseEndpoints(ep => ep.MapGrpcService<GrpcMemoryService>());
        }
    }
}
