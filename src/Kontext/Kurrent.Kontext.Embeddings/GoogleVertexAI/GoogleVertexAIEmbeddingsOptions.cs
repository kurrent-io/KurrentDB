// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Embeddings.GoogleVertexAI;

/// <summary>
/// Settings for the Google Vertex AI embedding generator. Settable properties with a parameterless
/// constructor, so it binds straight from configuration via <c>IConfiguration.Get&lt;T&gt;()</c> and supports
/// the <c>Action&lt;GoogleVertexAIEmbeddingsOptions&gt;</c> configuration pattern.
/// </summary>
public class GoogleVertexAIEmbeddingsOptions {
	public string? ProjectId { get; set; }

	public string? Region { get; set; }

	public string Model { get; set; } = "text-embedding-004";

	public int BatchSize { get; set; } = 100;
}
