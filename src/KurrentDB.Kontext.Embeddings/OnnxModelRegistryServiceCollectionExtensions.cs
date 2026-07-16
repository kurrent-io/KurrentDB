// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kurrent.Kontext.Embeddings;

/// <summary>
/// DI registration for the shared <see cref="OnnxModelRegistry"/>, bound from configuration. The section is
/// expected to carry a <c>ModelsDirectory</c> (the local cache root) and a <c>Models</c> array of
/// <see cref="OnnxModelManifest"/>:
/// <code>
/// "Kontext:Embeddings": {
///   "ModelsDirectory": "/var/lib/kontext/models",
///   "Models": [
///     { "Key": "multilingual-e5-small", "Model": "model.onnx", "Assets": [ "sentencepiece.bpe.model" ] }
///   ]
/// }
/// </code>
/// </summary>
public static class OnnxModelRegistryServiceCollectionExtensions {
	extension(IServiceCollection services) {
		/// <summary>
		/// Binds an <see cref="OnnxModelRegistry"/> from <paramref name="configuration"/> and registers it as a
		/// singleton so the embedding-generator DI helpers can resolve it.
		/// </summary>
		public IServiceCollection AddOnnxModelRegistry(IConfiguration configuration) =>
			services.AddSingleton(OnnxModelRegistry.FromConfiguration(configuration));
	}
}
