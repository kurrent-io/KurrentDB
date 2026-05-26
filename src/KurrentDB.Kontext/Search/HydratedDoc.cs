// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.Kontext.Search;

/// <summary>
/// A candidate hydrated from KurrentDB plus the optional MMR-similarity signals
/// (embedding or token set) attached later in the pipeline.
/// </summary>
public sealed class HydratedDoc {
	public required string Stream { get; init; }
	public required long? EventNumber { get; init; }
	public required string EventType { get; init; }
	public required DateTime Timestamp { get; init; }
	public required ReadOnlyMemory<byte> Data { get; init; }
	public required bool IsJson { get; init; }
	public required ReadOnlyMemory<byte> Metadata { get; init; }
	public ReadOnlyMemory<float> Embedding { get; set; }
	public HashSet<string>? Tokens { get; set; }
	public bool HasCommitRecord => !EventNumber.HasValue;
}