// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Embeddings.OpenAI;

/// <summary>
/// Settings for the OpenAI (and OpenAI-compatible) embedding generator. Settable properties with a
/// parameterless constructor, so it binds straight from configuration via <c>IConfiguration.Get&lt;T&gt;()</c>
/// and supports the <c>Action&lt;OpenAIEmbeddingsOptions&gt;</c> configuration pattern.
/// </summary>
public class OpenAIEmbeddingsOptions {
	public string? ApiKey { get; set; }

	public string Model { get; set; } = "text-embedding-3-small";

	// Optional — set for OpenAI-compatible proxies.
	public string? Endpoint { get; set; }

	public int BatchSize { get; set; } = 256;
}
