// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Embeddings.Aws;
using Kurrent.Kontext.Embeddings.GoogleVertexAI;
using Kurrent.Kontext.Embeddings.Ollama;
using Kurrent.Kontext.Embeddings.OpenAI;
using Kurrent.Kontext.Embeddings.SentencePieceOnnx;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kurrent.Kontext.Embeddings;

/// <summary>
/// The one provider switch: takes the typed pieces (never the host's config class, so the
/// dependency keeps pointing this way) and registers the selected backend. Every path returns
/// the <see cref="EmbeddingGeneratorBuilder{TInput,TEmbedding}"/> so callers keep the
/// decorator seam (caching, telemetry).
/// </summary>
public static class KontextEmbeddingsServiceCollectionExtensions {
	extension(IServiceCollection services) {
		public EmbeddingGeneratorBuilder<string, Embedding<float>> AddKontextEmbeddings(
			EmbeddingsProvider provider,
			LocalEmbeddingsOptions? local = null,
			OpenAIEmbeddingsOptions? openAI = null,
			OllamaEmbeddingsOptions? ollama = null,
			GoogleVertexAIEmbeddingsOptions? googleVertexAI = null,
			AmazonBedrockEmbeddingsOptions? amazonBedrock = null
		) => provider switch {
			EmbeddingsProvider.Local          => services.AddLocalOnnxEmbeddings(local ?? new()),
			EmbeddingsProvider.OpenAI         => services.AddOpenAIEmbeddings(openAI ?? new()),
			EmbeddingsProvider.Ollama         => services.AddOllamaEmbeddings(ollama ?? new()),
			EmbeddingsProvider.GoogleVertexAI => services.AddGoogleVertexAIEmbeddings(googleVertexAI ?? new()),
			EmbeddingsProvider.AmazonBedrock  => services.AddAmazonBedrockEmbeddings(amazonBedrock ?? new()),
			_ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unknown embeddings provider.")
		};

		/// <summary>
		/// The Local path: disk-cached models through the <see cref="OnnxModelRegistry"/> when one is
		/// configured; the shipped interim model otherwise — zero-config local embeddings either way.
		/// </summary>
        EmbeddingGeneratorBuilder<string, Embedding<float>> AddLocalOnnxEmbeddings(LocalEmbeddingsOptions options) {
			if (string.IsNullOrWhiteSpace(options.ModelsDirectory) && options.Models.Count == 0)
				return services.AddPmm12Embeddings();

			services.TryAddSingleton(new OnnxModelRegistry(options.ModelsDirectory, options.Models));

			return services.AddSentencePieceOnnxEmbeddings(o => o.ModelId = options.ModelId);
		}
	}

}
