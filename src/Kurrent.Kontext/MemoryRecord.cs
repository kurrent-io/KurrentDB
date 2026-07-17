// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Microsoft.Extensions.VectorData;

namespace Kurrent.Kontext;

/// <summary>
/// The vector-store row a memory is persisted as — the folded state of one memory, flattened to the
/// property types every <see cref="VectorStore"/> connector supports (scalars, string lists, byte
/// blobs). Lifecycle is stored as boolean flags alongside their timestamps because the portable
/// filter surface is property equality and list containment only — a "timestamp is unset" predicate
/// does not translate across connectors.
/// </summary>
sealed class MemoryRecord {
	/// <summary>
	/// Recall embedding width. Matches the 384-dimensional prototype models (all-MiniLM /
	/// multilingual-e5-small) and must match whatever <c>IEmbeddingGenerator</c> the host
	/// configures on the <see cref="VectorStore"/>.
	/// </summary>
	public const int EmbeddingDimensions = 384;

	[VectorStoreKey]
	public string MemoryId { get; set; } = "";

	// Enums are stored as their proto numeric values: they are wire-stable by contract, and numbers
	// keep the record within every connector's supported data types.
	[VectorStoreData(IsIndexed = true)]
	public int MemoryType { get; set; }

	[VectorStoreData(IsFullTextIndexed = true)]
	public string Content { get; set; } = "";

	[VectorStoreData(IsIndexed = true)]
	public int Importance { get; set; }

	[VectorStoreData]
	public int Sentiment { get; set; }

	[VectorStoreData]
	public int Urgency { get; set; }

	// Canonical encoded form ("scope:value", bare "value" when unscoped — see TagParser), so the
	// all-tags-present filter is plain list containment.
	[VectorStoreData(IsIndexed = true)]
	public List<string> Tags { get; set; } = [];

	// The full Evidence message as protobuf bytes — round-tripped for reads, never queried.
	[VectorStoreData]
	public byte[] Evidence { get; set; } = [];

	// Memory ids cited by Evidence, flattened out of the blob so the retract cascade can find
	// derived memories with a containment filter.
	[VectorStoreData(IsIndexed = true)]
	public List<string> CitedMemoryIds { get; set; } = [];

	[VectorStoreData]
	public List<string> Supersedes { get; set; } = [];

	[VectorStoreData]
	public DateTimeOffset? ValidityStart { get; set; }

	[VectorStoreData]
	public DateTimeOffset? ValidityEnd { get; set; }

	[VectorStoreData]
	public DateTimeOffset RetainedAt { get; set; }

	/// <summary>The recency clock — refreshed by every recall and reclaim (reconsolidation).</summary>
	[VectorStoreData]
	public DateTimeOffset LastAccessedAt { get; set; }

	[VectorStoreData(IsIndexed = true)]
	public bool IsRetracted { get; set; }

	[VectorStoreData]
	public DateTimeOffset? RetractedAt { get; set; }

	[VectorStoreData(IsIndexed = true)]
	public bool IsSuperseded { get; set; }

	[VectorStoreData]
	public DateTimeOffset? SupersededAt { get; set; }

	[VectorStoreData]
	public string SupersededBy { get; set; } = "";

	// String-typed vector property: the store's configured IEmbeddingGenerator turns this text into
	// the embedding on upsert/search, and the vector is never read back — so deriving from Content
	// adds no state and keeps the two in sync by construction.
	[VectorStoreVector(EmbeddingDimensions)]
	public string Embedding => Content;
}
