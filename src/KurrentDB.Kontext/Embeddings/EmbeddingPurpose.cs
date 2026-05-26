// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Microsoft.Extensions.AI;

namespace KurrentDB.Kontext.Embeddings;

/// <summary>
/// Some embedding models are asymmetric: documents and search queries must be embedded
/// differently (e.g. Cohere's input_type). Generators that don't distinguish ignore this.
/// </summary>
public static class EmbeddingPurpose {
	public const string Key = "purpose";
	public const string Document = "document";
	public const string Query = "query";

	/// <summary>Options for embedding a search query.</summary>
	public static readonly EmbeddingGenerationOptions QueryOptions = new() {
		AdditionalProperties = new() { [Key] = Query },
	};

	/// <summary>Options for embedding a document to be indexed (the default purpose).</summary>
	public static readonly EmbeddingGenerationOptions DocumentOptions = new() {
		AdditionalProperties = new() { [Key] = Document },
	};
}
