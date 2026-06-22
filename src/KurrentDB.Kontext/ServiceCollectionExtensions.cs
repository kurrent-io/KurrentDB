// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.ClientModel;
using System.Diagnostics.Metrics;
using Amazon;
using Amazon.BedrockRuntime;
using Google.Cloud.VertexAI.Extensions;
using KurrentDB.Core;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Kontext.Embeddings;
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
using OllamaSharp;
using OpenAI;

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
				services.TryAddSingleton(sp => new EmbeddingService(
					sp.GetRequiredService<ModelManager>(),
					sp.GetRequiredService<ILogger<EmbeddingService>>()));
				services.TryAddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
					sp => sp.GetRequiredService<EmbeddingService>());
				break;

			case EmbeddingsProvider.OpenAI:
				services.TryAddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
					_ => CreateOpenAiEmbeddingGenerator(config));
				break;

			case EmbeddingsProvider.Ollama:
				services.TryAddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
					_ => CreateOllamaEmbeddingGenerator(config));
				break;

			case EmbeddingsProvider.GoogleVertexAI:
				services.TryAddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
					_ => CreateGoogleVertexAIEmbeddingGenerator(config));
				break;

			case EmbeddingsProvider.AmazonBedrock:
				services.TryAddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
					_ => CreateAmazonBedrockEmbeddingGenerator(config));
				break;

			default:
				throw new NotSupportedException(
					$"Embeddings provider '{config.Provider}' is not supported.");
		}
	}

	internal static IEmbeddingGenerator<string, Embedding<float>> CreateOpenAiEmbeddingGenerator(KontextEmbeddingsConfig config) {
		var openAi = config.OpenAI;
		if (string.IsNullOrEmpty(openAi.ApiKey))
			throw new ArgumentException("Embeddings:OpenAI:ApiKey is required for the OpenAI provider.");

		var options = new OpenAIClientOptions();
		if (!string.IsNullOrEmpty(openAi.Endpoint))
			options.Endpoint = new Uri(openAi.Endpoint);

		var client = new OpenAIClient(new ApiKeyCredential(openAi.ApiKey), options);
		return client.GetEmbeddingClient(openAi.Model).AsIEmbeddingGenerator();
	}

	internal static IEmbeddingGenerator<string, Embedding<float>> CreateOllamaEmbeddingGenerator(KontextEmbeddingsConfig config) =>
		new OllamaApiClient(new Uri(config.Ollama.Endpoint), config.Ollama.Model);

	internal static IEmbeddingGenerator<string, Embedding<float>> CreateGoogleVertexAIEmbeddingGenerator(KontextEmbeddingsConfig config) {
		var vertex = config.GoogleVertexAI;
		if (string.IsNullOrEmpty(vertex.ProjectId))
			throw new ArgumentException("Embeddings:GoogleVertexAI:ProjectId is required for the GoogleVertexAI provider.");
		if (string.IsNullOrEmpty(vertex.Region))
			throw new ArgumentException("Embeddings:GoogleVertexAI:Region is required for the GoogleVertexAI provider.");

		var modelResource = Google.Cloud.AIPlatform.V1.EndpointName
			.FromProjectLocationPublisherModel(vertex.ProjectId, vertex.Region, "google", vertex.Model)
			.ToString();

		return new Google.Cloud.AIPlatform.V1.PredictionServiceClientBuilder {
			Endpoint = $"{vertex.Region}-aiplatform.googleapis.com",
		}.BuildIEmbeddingGenerator(defaultModelId: modelResource);
	}

	internal static IEmbeddingGenerator<string, Embedding<float>> CreateAmazonBedrockEmbeddingGenerator(KontextEmbeddingsConfig config) {
		var bedrock = config.AmazonBedrock;
		if (string.IsNullOrEmpty(bedrock.Region))
			throw new ArgumentException("Embeddings:AmazonBedrock:Region is required for the AmazonBedrock provider.");

		var runtime = new AmazonBedrockRuntimeClient(RegionEndpoint.GetBySystemName(bedrock.Region));

		// The AWS MEAI extension marshals Titan-format request bodies only; Cohere embed
		// models use a different request format and get a dedicated generator.
		return bedrock.Model.Contains("cohere.", StringComparison.OrdinalIgnoreCase)
			? new CohereBedrockEmbeddingGenerator(runtime, bedrock.Model)
			: runtime.AsIEmbeddingGenerator(defaultModelId: bedrock.Model);
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
