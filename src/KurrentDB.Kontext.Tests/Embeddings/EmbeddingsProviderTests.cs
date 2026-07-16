// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Embeddings.GoogleVertexAI;
using Kurrent.Kontext.Embeddings.Ollama;
using Kurrent.Kontext.Embeddings.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KurrentDB.Kontext.Tests.Embeddings;

public class EmbeddingsProviderTests {
	// The provider extensions validate and build the client lazily inside the DI factory, so a missing
	// required field surfaces when the generator is resolved, not when it is registered.
	static IEmbeddingGenerator<string, Embedding<float>> Resolve(Action<IServiceCollection> register) {
		var services = new ServiceCollection();
		register(services);
		return services.BuildServiceProvider().GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();
	}

	// -------- Required fields --------

	[Test]
	public async Task OpenAi_Requires_ApiKey() {
		var ex = Assert.Throws<ArgumentException>(() =>
			Resolve(services => services.AddOpenAIEmbeddings(new OpenAIEmbeddingsOptions())));
		await Assert.That(ex.Message).Contains("ApiKey");
	}

	[Test]
	public async Task GoogleVertexAI_Requires_ProjectId() {
		var ex = Assert.Throws<ArgumentException>(() =>
			Resolve(services => services.AddGoogleVertexAIEmbeddings(
				new GoogleVertexAIEmbeddingsOptions { Region = "us-central1" })));
		await Assert.That(ex.Message).Contains("ProjectId");
	}

	[Test]
	public async Task GoogleVertexAI_Requires_Region() {
		var ex = Assert.Throws<ArgumentException>(() =>
			Resolve(services => services.AddGoogleVertexAIEmbeddings(
				new GoogleVertexAIEmbeddingsOptions { ProjectId = "my-project" })));
		await Assert.That(ex.Message).Contains("Region");
	}

	[Test]
	public async Task AmazonBedrock_Requires_Region() {
		var ex = Assert.Throws<ArgumentException>(() =>
			Resolve(services => services.AddAmazonBedrockEmbeddings(new AmazonBedrockEmbeddingsOptions())));
		await Assert.That(ex.Message).Contains("Region");
	}

	// -------- Default fallbacks --------

	[Test]
	public async Task OpenAi_Defaults_Model_To_Text_Embedding_3_Small() {
		var generator = Resolve(services =>
			services.AddOpenAIEmbeddings(new OpenAIEmbeddingsOptions { ApiKey = "sk-fake" }));
		await Assert.That(GetMetadata(generator).DefaultModelId).IsEqualTo("text-embedding-3-small");
	}

	[Test]
	public async Task Ollama_Defaults_Endpoint_And_Model() {
		var options = new OllamaEmbeddingsOptions();
		await Assert.That(options.Endpoint).IsEqualTo("http://localhost:11434");
		await Assert.That(options.Model).IsEqualTo("nomic-embed-text");

		// The factory threads the model through to the resulting generator.
		var generator = Resolve(services => services.AddOllamaEmbeddings(new OllamaEmbeddingsOptions()));
		await Assert.That(GetMetadata(generator).DefaultModelId).IsEqualTo("nomic-embed-text");
	}

	static EmbeddingGeneratorMetadata GetMetadata(IEmbeddingGenerator<string, Embedding<float>> g) =>
		(EmbeddingGeneratorMetadata)g.GetService(typeof(EmbeddingGeneratorMetadata))!;

	// -------- DI dispatch --------

	[Test]
	public async Task Local_Provider_Registers_EmbeddingService() {
		var services = new ServiceCollection();
		services.AddSingleton(NullLoggerFactory.Instance);
		services.AddLogging();
		services.AddSingleton(new KontextEmbeddingsConfig { Provider = EmbeddingsProvider.Local });
		services.AddSingleton(_ => new ModelManager(NullLogger<ModelManager>.Instance));
		services.AddKontext();

		var sp = services.BuildServiceProvider();
		var generator = sp.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();
		await Assert.That(generator).IsTypeOf<EmbeddingService>();
	}

	[Test]
	public async Task OpenAi_Provider_Registers_IEmbeddingGenerator() {
		var services = new ServiceCollection();
		var config = new KontextEmbeddingsConfig {
			Provider = EmbeddingsProvider.OpenAI,
			OpenAI = { ApiKey = "sk-fake" },
		};
		services.AddSingleton(config);
		KontextServiceCollectionExtensions.RegisterEmbeddingsProvider(services, config);

		// The local ONNX EmbeddingService must not be registered for non-Local providers,
		// otherwise InitializeKontextAsync would try to load the model on startup.
		await Assert.That(services.Any(d => d.ServiceType == typeof(EmbeddingService))).IsFalse();
		await Assert.That(services.Any(d => d.ServiceType == typeof(IEmbeddingGenerator<string, Embedding<float>>))).IsTrue();
	}

	[Test]
	public async Task AmazonBedrock_Provider_Registers_IEmbeddingGenerator() {
		var services = new ServiceCollection();
		var config = new KontextEmbeddingsConfig {
			Provider = EmbeddingsProvider.AmazonBedrock,
			AmazonBedrock = { Region = "us-east-1" },
		};
		KontextServiceCollectionExtensions.RegisterEmbeddingsProvider(services, config);

		await Assert.That(services.Any(d => d.ServiceType == typeof(EmbeddingService))).IsFalse();
		await Assert.That(services.Any(d => d.ServiceType == typeof(IEmbeddingGenerator<string, Embedding<float>>))).IsTrue();
	}

	[Test]
	public async Task GoogleVertexAI_Provider_Registers_IEmbeddingGenerator() {
		var services = new ServiceCollection();
		var config = new KontextEmbeddingsConfig {
			Provider = EmbeddingsProvider.GoogleVertexAI,
			GoogleVertexAI = { ProjectId = "my-project", Region = "us-central1" },
		};
		KontextServiceCollectionExtensions.RegisterEmbeddingsProvider(services, config);

		await Assert.That(services.Any(d => d.ServiceType == typeof(EmbeddingService))).IsFalse();
		await Assert.That(services.Any(d => d.ServiceType == typeof(IEmbeddingGenerator<string, Embedding<float>>))).IsTrue();
	}

	[Test]
	public async Task Ollama_Provider_Registers_IEmbeddingGenerator() {
		var services = new ServiceCollection();
		var config = new KontextEmbeddingsConfig { Provider = EmbeddingsProvider.Ollama };
		KontextServiceCollectionExtensions.RegisterEmbeddingsProvider(services, config);

		await Assert.That(services.Any(d => d.ServiceType == typeof(EmbeddingService))).IsFalse();
		await Assert.That(services.Any(d => d.ServiceType == typeof(IEmbeddingGenerator<string, Embedding<float>>))).IsTrue();
	}

	[Test]
	public async Task AmazonBedrock_Routes_Cohere_Models_To_The_Cohere_Generator() {
		var generator = Resolve(services => services.AddAmazonBedrockEmbeddings(
			new AmazonBedrockEmbeddingsOptions { Region = "us-east-1", Model = "cohere.embed-english-v3" }));
		await Assert.That(generator).IsTypeOf<CohereBedrockEmbeddingGenerator>();
	}

	[Test]
	public async Task AmazonBedrock_Routes_Titan_Models_To_The_Aws_Generator() {
		var generator = Resolve(services => services.AddAmazonBedrockEmbeddings(
			new AmazonBedrockEmbeddingsOptions { Region = "us-east-1" })); // default amazon.titan-embed-text-v2:0
		await Assert.That(generator).IsNotTypeOf<CohereBedrockEmbeddingGenerator>();
		await Assert.That(GetMetadata(generator).DefaultModelId).IsEqualTo("amazon.titan-embed-text-v2:0");
	}

	[Test]
	public async Task Unsupported_Provider_Throws() {
		var services = new ServiceCollection();
		var config = new KontextEmbeddingsConfig { Provider = (EmbeddingsProvider)999 };
		var ex = Assert.Throws<NotSupportedException>(() =>
			KontextServiceCollectionExtensions.RegisterEmbeddingsProvider(services, config));
		await Assert.That(ex.Message).Contains("not supported");
	}
}
