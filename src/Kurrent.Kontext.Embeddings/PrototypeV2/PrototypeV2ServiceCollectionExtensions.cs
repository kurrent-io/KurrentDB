// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Kurrent.Kontext.Embeddings.PrototypeV2;

/// <summary>
/// Canonical DI registration for the default local embedding generator (implementation A′). Wraps
/// Microsoft.Extensions.AI's <c>AddEmbeddingGenerator</c> so it registers as a singleton
/// <see cref="IEmbeddingGenerator{TInput,TEmbedding}"/> and returns an
/// <see cref="EmbeddingGeneratorBuilder{TInput,TEmbedding}"/> for chaining the standard middleware.
/// </summary>
public static class PrototypeV2ServiceCollectionExtensions {
	extension(IServiceCollection services) {
		/// <summary>
		/// Registers the DEFAULT local embedding generator (implementation A′) — the hand-rolled WordPiece +
		/// all-MiniLM path that is byte-faithful to vectors already stored in existing indexes. Model
		/// distribution stays the caller's concern via <paramref name="modelSource"/>.
		/// </summary>
		public EmbeddingGeneratorBuilder<string, Embedding<float>> AddPrototypeV2Embeddings(
			Func<IServiceProvider, (Stream Model, Stream Vocab)> modelSource,
			Action<PrototypeV2Options>? configure = null) =>
			services.AddEmbeddingGenerator<string, Embedding<float>>(sp => {
				var options = new PrototypeV2Options();
				configure?.Invoke(options);
				var (model, vocab) = modelSource(sp);
				return new PrototypeV2EmbeddingGenerator(model, vocab, options);
			});
	}
}
