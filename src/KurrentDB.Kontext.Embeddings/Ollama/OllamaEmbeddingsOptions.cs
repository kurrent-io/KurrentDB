// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Embeddings.Ollama;

/// <summary>
/// Settings for the Ollama embedding generator. Settable properties with a parameterless constructor, so it
/// binds straight from configuration via <c>IConfiguration.Get&lt;T&gt;()</c> and supports the
/// <c>Action&lt;OllamaEmbeddingsOptions&gt;</c> configuration pattern.
/// </summary>
public class OllamaEmbeddingsOptions {
	public string Endpoint { get; set; } = "http://localhost:11434";

	public string Model { get; set; } = "nomic-embed-text";

	public int BatchSize { get; set; } = 16;
}
