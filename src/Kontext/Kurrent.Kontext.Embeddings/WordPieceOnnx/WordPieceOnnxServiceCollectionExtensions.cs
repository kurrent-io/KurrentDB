// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Kurrent.Kontext.Embeddings.WordPieceOnnx;

/// <summary>
/// Canonical DI registration for the BERT/WordPiece embedding generator (implementation B). Wraps
/// Microsoft.Extensions.AI's <c>AddEmbeddingGenerator</c> so it registers as a singleton
/// <see cref="IEmbeddingGenerator{TInput,TEmbedding}"/> and resolves the model at first use from the
/// <see cref="OnnxModelRegistry"/> registered in DI, keyed by <see cref="WordPieceOnnxOptions.ModelId"/>.
/// </summary>
public static class WordPieceOnnxServiceCollectionExtensions {
	extension(IServiceCollection services) {
		/// <summary>
		/// Registers the BERT/WordPiece generator (implementation B) as a singleton
		/// <see cref="IEmbeddingGenerator{TInput,TEmbedding}"/> — the same all-MiniLM model as A with a correct
		/// tokenizer that folds accents. Produces different vectors from A, so it is opt-in. The model is
		/// resolved from the registered <see cref="OnnxModelRegistry"/> by <see cref="WordPieceOnnxOptions.ModelId"/>.
		/// </summary>
		public EmbeddingGeneratorBuilder<string, Embedding<float>> AddWordPieceOnnxEmbeddings(
			Action<WordPieceOnnxOptions>? configure = null) =>
			services.AddEmbeddingGenerator<string, Embedding<float>>(sp => {
				var options = new WordPieceOnnxOptions();
				configure?.Invoke(options);
				return new WordPieceOnnxEmbeddingGenerator(sp.GetRequiredService<OnnxModelRegistry>(), options);
			});
	}
}
