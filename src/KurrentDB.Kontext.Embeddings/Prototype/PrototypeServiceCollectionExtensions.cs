// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Kurrent.Kontext.Embeddings.Prototype;

/// <summary>
/// DI registration for the local in-process ONNX prototype generator (all-MiniLM-L6-v2). Registers the
/// concrete <see cref="EmbeddingService"/> as well as the <see cref="IEmbeddingGenerator{TInput,TEmbedding}"/>
/// interface: startup preloads the ONNX model by resolving <see cref="EmbeddingService"/> directly.
/// <see cref="ModelManager"/> is expected to be registered already (it is also shared by the cross-encoder).
/// </summary>
public static class PrototypeServiceCollectionExtensions {
	extension(IServiceCollection services) {
		/// <summary>Registers the local ONNX embedding generator (implementation A) as the embedding generator.</summary>
		public IServiceCollection AddLocalEmbeddings() {
			services.TryAddSingleton(sp => new EmbeddingService(
				sp.GetRequiredService<ModelManager>(),
				sp.GetRequiredService<ILogger<EmbeddingService>>()));
			services.TryAddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
				sp => sp.GetRequiredService<EmbeddingService>());
			return services;
		}
	}
}
