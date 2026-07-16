// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using OllamaSharp;

namespace Kurrent.Kontext.Embeddings.Ollama;

/// <summary>
/// DI registration for the Ollama embedding generator. <see cref="OllamaApiClient"/> implements
/// <see cref="IEmbeddingGenerator{TInput,TEmbedding}"/> directly.
/// </summary>
public static class OllamaEmbeddingsServiceCollectionExtensions {
	extension(IServiceCollection services) {
		/// <summary>Registers the Ollama embedding generator from the given settings.</summary>
		public EmbeddingGeneratorBuilder<string, Embedding<float>> AddOllamaEmbeddings(OllamaEmbeddingsOptions options) =>
			services.AddEmbeddingGenerator<string, Embedding<float>>(
				_ => new OllamaApiClient(new Uri(options.Endpoint), options.Model));

		/// <summary>Registers the Ollama embedding generator, configuring defaults via <paramref name="configure"/>.</summary>
		public EmbeddingGeneratorBuilder<string, Embedding<float>> AddOllamaEmbeddings(Action<OllamaEmbeddingsOptions> configure) {
			var options = new OllamaEmbeddingsOptions();
			configure(options);
			return services.AddOllamaEmbeddings(options);
		}
	}
}
