// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Amazon;
using Amazon.BedrockRuntime;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Kurrent.Kontext.Embeddings.Aws;

/// <summary>
/// DI registration for the Amazon Bedrock embedding generator. Validation and client construction run
/// lazily inside the DI factory, so a missing region surfaces when the generator is resolved.
/// </summary>
public static class AmazonBedrockEmbeddingsServiceCollectionExtensions {
	extension(IServiceCollection services) {
		/// <summary>Registers the Amazon Bedrock embedding generator from the given settings.</summary>
		public EmbeddingGeneratorBuilder<string, Embedding<float>> AddAmazonBedrockEmbeddings(AmazonBedrockEmbeddingsOptions options) =>
			services.AddEmbeddingGenerator<string, Embedding<float>>(_ => {
				if (string.IsNullOrEmpty(options.Region))
					throw new ArgumentException("Embeddings:AmazonBedrock:Region is required for the AmazonBedrock provider.");

				var runtime = new AmazonBedrockRuntimeClient(RegionEndpoint.GetBySystemName(options.Region));

				// The AWS MEAI extension marshals Titan-format request bodies only; Cohere embed
				// models use a different request format and get a dedicated generator.
				return options.Model.Contains("cohere.", StringComparison.OrdinalIgnoreCase)
					? new CohereBedrockEmbeddingGenerator(runtime, options.Model)
					: runtime.AsIEmbeddingGenerator(defaultModelId: options.Model);
			});

		/// <summary>Registers the Amazon Bedrock embedding generator, configuring defaults via <paramref name="configure"/>.</summary>
		public EmbeddingGeneratorBuilder<string, Embedding<float>> AddAmazonBedrockEmbeddings(Action<AmazonBedrockEmbeddingsOptions> configure) {
			var options = new AmazonBedrockEmbeddingsOptions();
			configure(options);
			return services.AddAmazonBedrockEmbeddings(options);
		}
	}
}
