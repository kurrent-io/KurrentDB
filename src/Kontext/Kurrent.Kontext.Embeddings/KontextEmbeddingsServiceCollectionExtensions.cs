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

	extension(IEmbeddingGenerator<string, Embedding<float>> generator) {
		/// <summary>
		/// Fails fast when the generator's vectors do not match the expected dimension. Model names are
		/// free strings, so no lookup table stays correct — asking by trying is the only exact check,
		/// and a mismatch caught here is a startup error instead of a poisoned vector store.
		/// </summary>
		public async Task EnsureDimensionAsync(int expectedDimension, CancellationToken ct = default) {
			var generated = await generator.GenerateAsync(["kontext dimension probe"], cancellationToken: ct).ConfigureAwait(false);
			var actual    = generated[0].Vector.Length;

			if (actual != expectedDimension) {
				throw new InvalidOperationException(
					$"The embeddings provider produces {actual}-dimensional vectors but the configured dimension is "
				  + $"{expectedDimension}. The schema's FLOAT[{expectedDimension}] column would reject or poison every "
				  + "stored vector - align Embeddings:Dimension with the provider's model.");
			}
		}
	}
}
