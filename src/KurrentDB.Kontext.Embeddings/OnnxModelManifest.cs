// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Embeddings;

/// <summary>
/// Declarative description of one ONNX model: its registry key, the model file name, where to fetch it, and
/// the companion assets it ships with. One shape serving three consumers — the <see cref="OnnxModelRegistry"/>
/// (what models exist and how to open them), the generators (which assets to read), and the (future)
/// downloader (name + repo + asset list). A plain mutable settings class (parameterless, settable props) so it
/// binds from configuration without ceremony.
/// </summary>
public sealed class OnnxModelManifest {
	/// <summary>Registry lookup key and the model's identity, e.g. <c>multilingual-e5-small</c>.</summary>
	public string Key { get; set; } = "";

	/// <summary>The ONNX model file name within the repo's <c>onnx/</c> folder, e.g. <c>model.onnx</c>.</summary>
	public string Model { get; set; } = "";

	/// <summary>Optional source repository URL; consumed by the (future) downloader, not by reading.</summary>
	public string? RepoUrl { get; set; }

	/// <summary>Companion asset file names shipped alongside the model (tokenizer files, etc.).</summary>
	public IReadOnlyList<string> Assets { get; set; } = [];
}
