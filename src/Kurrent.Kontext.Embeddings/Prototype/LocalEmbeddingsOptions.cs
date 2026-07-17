// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Embeddings.Prototype;

/// <summary>
/// Settings for the local in-process ONNX embedding generator (all-MiniLM-L6-v2). Settable properties with a
/// parameterless constructor, so it binds straight from configuration via <c>IConfiguration.Get&lt;T&gt;()</c>.
/// </summary>
public class LocalEmbeddingsOptions {
	// ONNX supports batching but it spikes CPU for little gain.
	public int BatchSize { get; set; } = 1;
}
