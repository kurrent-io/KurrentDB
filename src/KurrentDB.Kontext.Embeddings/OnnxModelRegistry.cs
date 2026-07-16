// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Microsoft.Extensions.Configuration;

namespace Kurrent.Kontext.Embeddings;

/// <summary>
/// Resolves a model key into a lazy <see cref="OnnxModel"/> from the registered manifests. Register models in
/// code (<see cref="Add"/> / the constructor) or bind them from configuration (<see cref="FromConfiguration"/>);
/// <see cref="Get"/> is cheap — it hands back a handle, and no bytes move until a generator reads from it.
/// Assets for key <c>k</c> live under <c>&lt;baseDirectory&gt;/&lt;k&gt;</c>.
/// </summary>
public sealed class OnnxModelRegistry {
	readonly string _baseDirectory;
	readonly Dictionary<string, OnnxModelManifest> _manifests = [];

	public OnnxModelRegistry(string baseDirectory, IEnumerable<OnnxModelManifest>? manifests = null) {
		_baseDirectory = baseDirectory;
		if (manifests is null) return;
		foreach (var manifest in manifests)
			_manifests[manifest.Key] = manifest;
	}

	/// <summary>Registers (or replaces) a model manifest. Returns this registry for chaining.</summary>
	public OnnxModelRegistry Add(OnnxModelManifest manifest) {
		_manifests[manifest.Key] = manifest;
		return this;
	}

	/// <summary>Resolves a model by key into a lazy <see cref="OnnxModel"/>. Throws if the key is not registered.</summary>
	public OnnxModel Get(string key) {
		if (!_manifests.TryGetValue(key, out var manifest))
			throw new KeyNotFoundException($"No ONNX model registered under key '{key}'.");
		return OnnxModel.FromManifest(manifest, Path.Combine(_baseDirectory, key));
	}

	/// <summary>
	/// Builds a registry from a configuration section carrying a <c>ModelsDirectory</c> (the local cache root)
	/// and a <c>Models</c> array of <see cref="OnnxModelManifest"/>. Binding is source-generated, so it is
	/// reflection-free / AOT-safe.
	/// </summary>
	public static OnnxModelRegistry FromConfiguration(IConfiguration configuration) {
		var baseDirectory = configuration["ModelsDirectory"]
			?? throw new InvalidOperationException(
				"Embeddings configuration is missing 'ModelsDirectory' (the local model cache root).");
		var manifests = configuration.GetSection("Models").Get<OnnxModelManifest[]>() ?? [];
		return new OnnxModelRegistry(baseDirectory, manifests);
	}
}
