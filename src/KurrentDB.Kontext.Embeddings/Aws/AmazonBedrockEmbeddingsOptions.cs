// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Embeddings.Aws;

/// <summary>
/// Settings for the Amazon Bedrock embedding generator. Settable properties with a parameterless constructor,
/// so it binds straight from configuration via <c>IConfiguration.Get&lt;T&gt;()</c> and supports the
/// <c>Action&lt;AmazonBedrockEmbeddingsOptions&gt;</c> configuration pattern.
/// </summary>
public class AmazonBedrockEmbeddingsOptions {
	public string? Region { get; set; }

	public string Model { get; set; } = "amazon.titan-embed-text-v2:0";

	public int BatchSize { get; set; } = 96;
}
