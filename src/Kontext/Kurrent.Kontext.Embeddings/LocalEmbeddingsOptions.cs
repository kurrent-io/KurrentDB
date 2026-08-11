// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Embeddings;

/// <summary>
/// The Local provider's block: which model files, from where. With a <see cref="ModelsDirectory"/>
/// or <see cref="Models"/> configured, the generator reads disk-cached models through the
/// <see cref="OnnxModelRegistry"/>; left empty, the shipped interim model is used — zero-config
/// local embeddings. A plain mutable settings class so it binds from configuration.
/// </summary>
public sealed class LocalEmbeddingsOptions {
	/// <summary>The local model cache root. Empty means no registry — the shipped interim model runs.</summary>
	public string ModelsDirectory { get; set; } = "";

	/// <summary>Which registered model to embed with, when the registry is configured.</summary>
	public string ModelId { get; set; } = "pmm12";

	/// <summary>The model manifests available under <see cref="ModelsDirectory"/>.</summary>
	public List<OnnxModelManifest> Models { get; set; } = [];

	public int BatchSize { get; set; } = 1;
}
