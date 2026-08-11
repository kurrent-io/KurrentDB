// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Embeddings;

/// <summary>
/// The embedding backends this library ships. The discriminator lives beside the providers it
/// discriminates: adding a backend touches this enum, its options class, and the
/// <see cref="KontextEmbeddingsServiceCollectionExtensions.AddKontextEmbeddings"/> switch — one assembly.
/// </summary>
public enum EmbeddingsProvider {
	/// <summary>In-process ONNX. 384-dim multilingual by default, CPU-only, no API key required.</summary>
	Local,
	OpenAI,
	Ollama,
	GoogleVertexAI,
	AmazonBedrock,
}
