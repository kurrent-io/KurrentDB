// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Reflection;

namespace Kurrent.Kontext.Embeddings;

/// <summary>
/// A resolved, lazy handle to one model's files. <see cref="ReadModel"/> is first-class — every generator
/// needs exactly one ONNX model — while companion files (tokenizer vocabs, SentencePiece models, …) are
/// opened by name via <see cref="ReadAsset"/>. Nothing is read until a generator asks for it, so obtaining a
/// handle is cheap; the returned streams are the caller's to dispose.
/// </summary>
public sealed class OnnxModel {
	readonly Func<Stream> _openModel;
	readonly Func<string, Stream> _openAsset;

	OnnxModel(string name, Func<Stream> openModel, Func<string, Stream> openAsset) {
		Name = name;
		_openModel = openModel;
		_openAsset = openAsset;
	}

	/// <summary>The model's identity (e.g. <c>multilingual-e5-small</c>); surfaced as the generator's model id.</summary>
	public string Name { get; }

	/// <summary>Opens the ONNX model file. The caller disposes the returned stream.</summary>
	public Stream ReadModel() => _openModel();

	/// <summary>Opens a named companion asset (e.g. a tokenizer file). The caller disposes the returned stream.</summary>
	public Stream ReadAsset(string asset) => _openAsset(asset);

	/// <summary>
	/// Builds a model from explicit file paths — the ONNX model plus a name→path map for its companion assets.
	/// Handy for tests and one-off tools where the files don't follow a registry layout.
	/// </summary>
	public static OnnxModel FromFiles(string name, string modelPath, IReadOnlyDictionary<string, string> assets) =>
		new(name, () => File.OpenRead(modelPath), asset => File.OpenRead(assets[asset]));

	/// <summary>
	/// Builds a model from a manifest rooted at <paramref name="directory"/>: the model at
	/// <c>&lt;directory&gt;/onnx/&lt;Model&gt;</c> and each companion asset at <c>&lt;directory&gt;/&lt;asset&gt;</c>
	/// (mirroring the source repo layout).
	/// </summary>
	public static OnnxModel FromManifest(OnnxModelManifest manifest, string directory) =>
		new(manifest.Key,
			() => File.OpenRead(Path.Combine(directory, "onnx", manifest.Model)),
			asset => File.OpenRead(Path.Combine(directory, asset)));

	/// <summary>
	/// Builds a model from an assembly's embedded manifest resources: the ONNX model at
	/// <paramref name="modelResource"/> and each companion asset resolved through
	/// <paramref name="assetResources"/> (asset name → resource logical-name). Generic over
	/// <paramref name="assembly"/>, so it knows nothing about which assembly ships the models. AOT-safe — it
	/// only calls <see cref="Assembly.GetManifestResourceStream(string)"/>, never <c>Assembly.Load</c>.
	/// </summary>
	public static OnnxModel FromEmbeddedResources(
		string name,
		Assembly assembly,
		string modelResource,
		IReadOnlyDictionary<string, string> assetResources)
	{
		return new OnnxModel(name,
			() => OpenResource(assembly, modelResource),
			asset => OpenResource(assembly, assetResources[asset]));
		
		static Stream OpenResource(Assembly assembly, string resource) =>
			assembly.GetManifestResourceStream(resource)
			?? throw new InvalidOperationException(
				$"Embedded resource '{resource}' not found in {assembly.GetName().Name}.");
	}
}
