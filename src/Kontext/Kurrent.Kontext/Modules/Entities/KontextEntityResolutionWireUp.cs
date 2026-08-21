// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.ClientModel;
using Kurrent.Kontext.Embeddings;
using Kurrent.Kontext.Embeddings.GlinerOnnx;
using Kurrent.Kontext.Infrastructure.Data;
using Kurrent.Kontext.Modules.Entities.Extraction;
using Kurrent.Surge;
using Kurrent.Surge.Configuration;
using Kurrent.Surge.Consumers.Configuration;
using Kurrent.Surge.Processors;
using Kurrent.Surge.Processors.Configuration;
using Kurrent.Surge.Producers.Configuration;
using KurrentDB.Core.Hosting;
using KurrentDB.Surge.Hosting;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenAI;

namespace Kurrent.Kontext.Modules.Entities;

static class KontextEntityResolutionWireUp {
    public static IServiceCollection AddKontextEntityResolution(this IServiceCollection services) {
        const string serviceName = "KontextEntityResolution";

        services.TryAddSingleton<IChatClient>(ctx => {
            var options = ctx.GetRequiredService<KontextOptions>().LLM;

            if (string.IsNullOrEmpty(options.Model))
                throw new InvalidOperationException("Kontext:LLM:Model is required.");

            if (options.ApiKey is null && options.Endpoint is null)
                throw new InvalidOperationException("Kontext:LLM:ApiKey or Kontext:LLM:Endpoint is required.");

            return new OpenAIClient(
		            new ApiKeyCredential(options.ApiKey ?? " "),
		            new OpenAIClientOptions { Endpoint = options.Endpoint })
	            .GetChatClient(options.Model)
	            .AsIChatClient();
        });

        services.TryAddSingleton<IEntityExtractor>(ctx => {
            var registry = ctx.GetService<OnnxModelRegistry>()
                ?? throw new InvalidOperationException("Kontext:Embeddings:Local:ModelsDirectory is required.");

            if (!registry.Contains(GlinerOnnxEntityRecognizer.DefaultModelId))
                registry.Add(DefaultGlinerManifest);

            return new EntityExtractor.Pipeline([
	            EntityExtractor.Gliner.Create(new GlinerOnnxEntityRecognizer(registry)),
	            EntityExtractor.Llm.Create(options => options.Chat = ctx.GetRequiredService<IChatClient>()),
            ], ctx.GetRequiredService<ILoggerFactory>().CreateLogger<EntityExtractor.Pipeline>());
        });

        services.AddNodeSystemInfoProvider();

        services.AddSingleton(ctx => new KontextEntityResolver(
            ctx.GetRequiredService<KontextDataSource>(),
            ctx.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>()));

        services.AddSingleton(ctx => new KontextEntityResolution(
            ctx.GetRequiredService<KontextEntityResolver>(),
            ctx.GetRequiredService<IEntityExtractor>(),
            ctx.GetRequiredService<IProducerBuilder>()));

        return services.AddSingleton<IHostedService, KontextEntityResolutionService>(ctx => {
            return new KontextEntityResolutionService(() => {
                var processor = ctx.GetRequiredService<IProcessorBuilder>()
                    .ProcessorId(serviceName)
                    .Logging(new LoggingOptions {
                        Enabled       = true,
                        LoggerFactory = ctx.GetRequiredService<ILoggerFactory>(),
                        LogName       = "Kurrent.Surge.Processors.SystemProcessor",
                    })
                    .DisableAutoLock()
                    .AutoCommit(new AutoCommitOptions {
                        Enabled          = true,
                        RecordsThreshold = 1000,
                        Interval         = TimeSpan.FromSeconds(5),
                        StreamTemplate   = KontextConventions.Streams.EntityResolutionCheckpointsStream,
                    })
                    .Filter(KontextConventions.Filters.MemoriesFilter)
                    .DisablePublishStateChanges()
                    .InitialPosition(SubscriptionInitialPosition.Earliest)
                    .WithModule(ctx.GetRequiredService<KontextEntityResolution>())
                    .Create();

                return processor;
            }, ctx, serviceName);
        });
    }

    static OnnxModelManifest DefaultGlinerManifest => new() {
        Key     = GlinerOnnxEntityRecognizer.DefaultModelId,
        Model   = "model_quantized.onnx",
        RepoUrl = "https://huggingface.co/onnx-community/gliner_small-v2.1",
        Assets  = ["spm.model"],
    };
}

class KontextEntityResolutionService(Func<IProcessor> getProcessor, IServiceProvider serviceProvider, string serviceName)
    : LeaderNodeProcessorWorker<IProcessor>(getProcessor, serviceProvider, serviceName);
