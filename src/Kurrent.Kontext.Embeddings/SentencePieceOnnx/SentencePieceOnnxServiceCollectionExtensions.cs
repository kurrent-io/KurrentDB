// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Kurrent.Kontext.Embeddings.SentencePieceOnnx;

/// <summary>
/// Canonical DI registration for the SentencePiece / XLM-R multilingual embedding generator (implementation
/// C). Wraps Microsoft.Extensions.AI's <c>AddEmbeddingGenerator</c> and resolves the model at first use from
/// the <see cref="OnnxModelRegistry"/> registered in DI, keyed by <see cref="SentencePieceOnnxOptions.ModelId"/>.
/// </summary>
public static class SentencePieceOnnxServiceCollectionExtensions {
	extension(IServiceCollection services) {
		/// <summary>
		/// Registers the SentencePiece / XLM-R generator (implementation C, multilingual) as a singleton
		/// <see cref="IEmbeddingGenerator{TInput,TEmbedding}"/>. The model is resolved from the registered
		/// <see cref="OnnxModelRegistry"/> by <see cref="SentencePieceOnnxOptions.ModelId"/>.
		/// </summary>
		public EmbeddingGeneratorBuilder<string, Embedding<float>> AddSentencePieceOnnxEmbeddings(
			Action<SentencePieceOnnxOptions>? configure = null) =>
			services.AddEmbeddingGenerator<string, Embedding<float>>(sp => {
				var options = new SentencePieceOnnxOptions();
				configure?.Invoke(options);
				return new SentencePieceOnnxEmbeddingGenerator(sp.GetRequiredService<OnnxModelRegistry>(), options);
			});
	}
}
