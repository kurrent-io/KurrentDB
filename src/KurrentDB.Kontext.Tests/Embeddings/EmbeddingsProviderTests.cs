// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KurrentDB.Kontext.Tests.Embeddings;

public class EmbeddingsProviderTests {
	// -------- Required fields --------

	[Test]
	public async Task OpenAi_Requires_ApiKey() {
		var ex = Assert.Throws<ArgumentException>(() =>
			KontextServiceCollectionExtensions.CreateOpenAiEmbeddingGenerator(
				new KontextEmbeddingsConfig { Provider = EmbeddingsProvider.OpenAI }));
		await Assert.That(ex.Message).Contains("ApiKey");
	}

	[Test]
	public async Task GoogleVertexAI_Requires_ProjectId() {
		var ex = Assert.Throws<ArgumentException>(() =>
			KontextServiceCollectionExtensions.CreateGoogleVertexAIEmbeddingGenerator(
				new KontextEmbeddingsConfig {
					Provider = EmbeddingsProvider.GoogleVertexAI,
					GoogleVertexAI = { Region = "us-central1" },
				}));
		await Assert.That(ex.Message).Contains("ProjectId");
	}

	[Test]
	public async Task GoogleVertexAI_Requires_Region() {
		var ex = Assert.Throws<ArgumentException>(() =>
			KontextServiceCollectionExtensions.CreateGoogleVertexAIEmbeddingGenerator(
				new KontextEmbeddingsConfig {
					Provider = EmbeddingsProvider.GoogleVertexAI,
					GoogleVertexAI = { ProjectId = "my-project" },
				}));
		await Assert.That(ex.Message).Contains("Region");
	}

	[Test]
	public async Task AmazonBedrock_Requires_Region() {
		var ex = Assert.Throws<ArgumentException>(() =>
			KontextServiceCollectionExtensions.CreateAmazonBedrockEmbeddingGenerator(
				new KontextEmbeddingsConfig { Provider = EmbeddingsProvider.AmazonBedrock }));
		await Assert.That(ex.Message).Contains("Region");
	}

	// -------- Default fallbacks --------

	[Test]
	public async Task OpenAi_Defaults_Model_To_Text_Embedding_3_Small() {
		var generator = KontextServiceCollectionExtensions.CreateOpenAiEmbeddingGenerator(
			new KontextEmbeddingsConfig {
				Provider = EmbeddingsProvider.OpenAI,
				OpenAI = { ApiKey = "sk-fake" },
			});
		await Assert.That(GetMetadata(generator).DefaultModelId).IsEqualTo("text-embedding-3-small");
	}

	[Test]
	public async Task Ollama_Defaults_Endpoint_And_Model() {
		var config = new OllamaEmbeddingsConfig();
		await Assert.That(config.Endpoint).IsEqualTo("http://localhost:11434");
		await Assert.That(config.Model).IsEqualTo("nomic-embed-text");

		// The factory threads the model through to the resulting generator.
		var generator = KontextServiceCollectionExtensions.CreateOllamaEmbeddingGenerator(
			new KontextEmbeddingsConfig { Provider = EmbeddingsProvider.Ollama });
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
		var generator = KontextServiceCollectionExtensions.CreateAmazonBedrockEmbeddingGenerator(
			new KontextEmbeddingsConfig {
				Provider = EmbeddingsProvider.AmazonBedrock,
				AmazonBedrock = { Region = "us-east-1", Model = "cohere.embed-english-v3" },
			});
		await Assert.That(generator).IsTypeOf<CohereBedrockEmbeddingGenerator>();
	}

	[Test]
	public async Task AmazonBedrock_Routes_Titan_Models_To_The_Aws_Generator() {
		var generator = KontextServiceCollectionExtensions.CreateAmazonBedrockEmbeddingGenerator(
			new KontextEmbeddingsConfig {
				Provider = EmbeddingsProvider.AmazonBedrock,
				AmazonBedrock = { Region = "us-east-1" }, // default amazon.titan-embed-text-v2:0
			});
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