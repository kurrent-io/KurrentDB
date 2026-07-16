// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics.Metrics;
using KurrentDB.Core;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Kontext.Embeddings;
using Kurrent.Kontext.Embeddings.Aws;
using Kurrent.Kontext.Embeddings.GoogleVertexAI;
using Kurrent.Kontext.Embeddings.Ollama;
using Kurrent.Kontext.Embeddings.OpenAI;
using Kurrent.Kontext.Embeddings.Prototype;
using KurrentDB.Kontext.Diagnostics;
using KurrentDB.Kontext.Indexing;
using KurrentDB.Kontext.Mcp;
using KurrentDB.Kontext.Mcp.Memory;
using KurrentDB.Kontext.Mcp.Inquiry;
using KurrentDB.Kontext.Mcp.Workspace;
using KurrentDB.Kontext.Search;
using KurrentDB.Kontext.Workspaces.Api;
using KurrentDB.Kontext.Workspaces.ControlPlane;
using KurrentDB.Kontext.Workspaces.Registry;
using KurrentDB.Kontext.Workspaces.Runtime;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace KurrentDB.Kontext;

public static class KontextServiceCollectionExtensions {
	public static IMcpServerBuilder AddKontext(this IServiceCollection services) {
		RegisterConfig(services);
		RegisterTelemetry(services);
		RegisterMLPipeline(services);
		RegisterSearch(services);
		RegisterWorkspaces(services);
		return RegisterMcp(services);
	}

	static void RegisterConfig(IServiceCollection services) {
		services.TryAddSingleton<KontextStorageConfig>();
		services.TryAddSingleton<KontextEmbeddingsConfig>();
	}

	static void RegisterTelemetry(IServiceCollection services) {
		var meter = new Meter(KontextConstants.MeterName, "1.0.0");
		services.AddKeyedSingleton(KontextConstants.InjectionKey, meter);
	}

	static void RegisterMLPipeline(IServiceCollection services) {
		services.TryAddSingleton(sp => new ModelManager(
			sp.GetRequiredService<ILogger<ModelManager>>()));
		services.TryAddSingleton(sp => new NounPhraseExtractor(
			sp.GetRequiredService<ILogger<NounPhraseExtractor>>()));
		services.TryAddSingleton(sp => new CrossEncoderService(
			sp.GetRequiredService<ModelManager>(),
			sp.GetRequiredService<ILogger<CrossEncoderService>>()));

		var embeddingsConfig = services.LastOrDefault(d => d.ServiceType == typeof(KontextEmbeddingsConfig))
			?.ImplementationInstance as KontextEmbeddingsConfig ?? new KontextEmbeddingsConfig();
		RegisterEmbeddingsProvider(services, embeddingsConfig);
	}

	static void RegisterSearch(IServiceCollection services) {
		services.TryAddSingleton<KontextReadySignal>();
		services.TryAddSingleton<StoreRegistry<IFtsStore>>();
		services.TryAddSingleton<StoreRegistry<IVectorStore>>();
		services.TryAddSingleton<EmbeddingCache>();
		services.TryAddSingleton<IKontextService>(sp => new KontextService(
			sp.GetRequiredService<IIndexBackend<string>>(),
			sp.GetRequiredService<IReadIndex<string>>(),
			new Retriever(
				sp.GetRequiredService<StoreRegistry<IFtsStore>>(),
				sp.GetRequiredService<StoreRegistry<IVectorStore>>()),
			sp.GetRequiredService<WorkspaceRegistry>(),
			sp.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>(),
			sp.GetRequiredService<CrossEncoderService>(),
			sp.GetRequiredService<NounPhraseExtractor>(),
			sp.GetRequiredService<KontextReadySignal>()));
	}

	static void RegisterWorkspaces(IServiceCollection services) {
		services.TryAddSingleton<VectorIndexMetadataSource>();
		services.TryAddSingleton<WorkspaceRegistry>();
		services.TryAddSingleton<WorkspaceStreamNameMap>();
		services.TryAddSingleton<WorkspaceEventStore>();
		services.TryAddSingleton<WorkspaceContext>();
		services.TryAddSingleton(sp => new WorkspaceLifecycleManager(
			sp.GetRequiredService<KontextStorageConfig>(),
			sp.GetRequiredService<KontextEmbeddingsConfig>(),
			sp.GetRequiredService<ISystemClient>(),
			sp.GetRequiredService<StoreRegistry<IFtsStore>>(),
			sp.GetRequiredService<StoreRegistry<IVectorStore>>(),
			sp.GetRequiredService<VectorIndexMetadataSource>(),
			sp.GetRequiredService<NounPhraseExtractor>(),
			sp.GetRequiredService<EmbeddingCache>(),
			sp.GetRequiredKeyedService<Meter>(KontextConstants.InjectionKey),
			writerCheckpoint: sp.GetRequiredService<GetWriterCheckpoint>().Invoke,
			sp.GetRequiredService<ILoggerFactory>()));
		services.AddCommandService<WorkspaceCommandService, WorkspaceState>();
		services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, WorkspaceLifecycleManager>(
			sp => sp.GetRequiredService<WorkspaceLifecycleManager>()));
		services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, WorkspaceProjection>());
	}

	internal static void RegisterEmbeddingsProvider(IServiceCollection services, KontextEmbeddingsConfig config) {
		switch (config.Provider) {
			case EmbeddingsProvider.Local:
				services.AddLocalEmbeddings();
				break;

			case EmbeddingsProvider.OpenAI:
				services.AddOpenAIEmbeddings(config.OpenAI);
				break;

			case EmbeddingsProvider.Ollama:
				services.AddOllamaEmbeddings(config.Ollama);
				break;

			case EmbeddingsProvider.GoogleVertexAI:
				services.AddGoogleVertexAIEmbeddings(config.GoogleVertexAI);
				break;

			case EmbeddingsProvider.AmazonBedrock:
				services.AddAmazonBedrockEmbeddings(config.AmazonBedrock);
				break;

			default:
				throw new NotSupportedException(
					$"Embeddings provider '{config.Provider}' is not supported.");
		}
	}

	static IMcpServerBuilder RegisterMcp(IServiceCollection services) {
		services.TryAddSingleton<InquiryManager>();
		services.TryAddSingleton<ActiveMcpSessions>();

		var toolOptions = new JsonSerializerOptions(McpJsonUtilities.DefaultOptions);
		toolOptions.Converters.Add(new RawJsonBytesConverter());
		toolOptions.MakeReadOnly();

		return services
			.AddMcpServer(options => { options.ServerInstructions = ServerInstructions.Text; })
			.WithTools<StatusTool>(toolOptions)
			.WithTools<ImportTool>(toolOptions)
			.WithTools<ManagementTool>(toolOptions)
			.WithTools<StreamsTool>(toolOptions)
			.WithTools<NewInquiryTool>(toolOptions)
			.WithTools<SearchTool>(toolOptions)
			.WithTools<ReadTool>(toolOptions)
			.WithTools<ForgetTool>(toolOptions)
			.WithTools<ViewTool>(toolOptions)
			.WithTools<EndInquiryTool>(toolOptions)
			.WithTools<RecallTool>(toolOptions)
			.WithTools<RetainTool>(toolOptions)
			.WithTools<TopicsTool>(toolOptions)
			.WithRequestFilters(filters => filters.AddCallToolFilter(next => async (context, ct) => {
				try {
					return await next(context, ct);
				} catch (ClientFacingException ex) {
					// Surface expected, caller-actionable errors to the agent instead of the SDK's
					// generic "An error occurred invoking '<tool>'." so it can recover (e.g. start
					// the workspace). Unexpected exceptions fall through to the generic error so
					// internal details aren't leaked.
					return new CallToolResult {
						IsError = true,
						Content = [new TextContentBlock { Text = ex.Message }],
					};
				}
			}));
	}
}
