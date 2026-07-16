// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.ClientModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using OpenAI;

namespace Kurrent.Kontext.Embeddings.OpenAI;

/// <summary>
/// DI registration for the OpenAI (and OpenAI-compatible) embedding generator. Validation and client
/// construction run lazily inside the DI factory, so a missing API key surfaces when the generator is
/// resolved, not when the service is registered.
/// </summary>
public static class OpenAIEmbeddingsServiceCollectionExtensions {
	extension(IServiceCollection services) {
		/// <summary>Registers the OpenAI embedding generator from the given settings.</summary>
		public EmbeddingGeneratorBuilder<string, Embedding<float>> AddOpenAIEmbeddings(OpenAIEmbeddingsOptions options) =>
			services.AddEmbeddingGenerator<string, Embedding<float>>(_ => {
				if (string.IsNullOrEmpty(options.ApiKey))
					throw new ArgumentException("Embeddings:OpenAI:ApiKey is required for the OpenAI provider.");

				var clientOptions = new OpenAIClientOptions();
				if (!string.IsNullOrEmpty(options.Endpoint))
					clientOptions.Endpoint = new Uri(options.Endpoint);

				var client = new OpenAIClient(new ApiKeyCredential(options.ApiKey), clientOptions);
				return client.GetEmbeddingClient(options.Model).AsIEmbeddingGenerator();
			});

		/// <summary>Registers the OpenAI embedding generator, configuring defaults via <paramref name="configure"/>.</summary>
		public EmbeddingGeneratorBuilder<string, Embedding<float>> AddOpenAIEmbeddings(Action<OpenAIEmbeddingsOptions> configure) {
			var options = new OpenAIEmbeddingsOptions();
			configure(options);
			return services.AddOpenAIEmbeddings(options);
		}
	}
}
