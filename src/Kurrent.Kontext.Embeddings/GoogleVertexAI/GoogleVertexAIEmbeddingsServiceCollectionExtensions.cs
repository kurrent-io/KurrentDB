// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Google.Cloud.VertexAI.Extensions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Kurrent.Kontext.Embeddings.GoogleVertexAI;

/// <summary>
/// DI registration for the Google Vertex AI embedding generator. Validation and client construction run
/// lazily inside the DI factory, so missing required settings surface when the generator is resolved.
/// </summary>
public static class GoogleVertexAIEmbeddingsServiceCollectionExtensions {
	extension(IServiceCollection services) {
		/// <summary>Registers the Google Vertex AI embedding generator from the given settings.</summary>
		public EmbeddingGeneratorBuilder<string, Embedding<float>> AddGoogleVertexAIEmbeddings(GoogleVertexAIEmbeddingsOptions options) =>
			services.AddEmbeddingGenerator<string, Embedding<float>>(_ => {
				if (string.IsNullOrEmpty(options.ProjectId))
					throw new ArgumentException("Embeddings:GoogleVertexAI:ProjectId is required for the GoogleVertexAI provider.");
				if (string.IsNullOrEmpty(options.Region))
					throw new ArgumentException("Embeddings:GoogleVertexAI:Region is required for the GoogleVertexAI provider.");

				var modelResource = Google.Cloud.AIPlatform.V1.EndpointName
					.FromProjectLocationPublisherModel(options.ProjectId, options.Region, "google", options.Model)
					.ToString();

				return new Google.Cloud.AIPlatform.V1.PredictionServiceClientBuilder {
					Endpoint = $"{options.Region}-aiplatform.googleapis.com",
				}.BuildIEmbeddingGenerator(defaultModelId: modelResource);
			});

		/// <summary>Registers the Google Vertex AI embedding generator, configuring defaults via <paramref name="configure"/>.</summary>
		public EmbeddingGeneratorBuilder<string, Embedding<float>> AddGoogleVertexAIEmbeddings(Action<GoogleVertexAIEmbeddingsOptions> configure) {
			var options = new GoogleVertexAIEmbeddingsOptions();
			configure(options);
			return services.AddGoogleVertexAIEmbeddings(options);
		}
	}
}
