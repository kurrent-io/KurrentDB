// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Core;

/// <summary>
/// The core memory service — the transport-agnostic port that holds all of Kontext's memory logic. The gRPC
/// and MCP edges are thin adapters that map their wire types to and from these domain shapes; this interface
/// never references generated or transport types. It mirrors the v3 <c>MemoryService</c> contract (see
/// <c>kurrentdb/kontext/v3/memory/memory.proto</c>), with per-operation inputs flattened to parameters.
/// </summary>
public interface IKontextMemory {
	/// <summary>
	/// Store one or more memories. Writing and additive — never overwrites or deletes; each memory without a
	/// pre-set id creates a new record. Not idempotent. Set <paramref name="reconcile"/> to also get back, per
	/// stored memory, existing memories it looks to duplicate or contradict (advisory; never blocks the write).
	/// </summary>
	ValueTask<RetainResult> RetainAsync(
		IReadOnlyList<Memory> memories, bool reconcile = false, CancellationToken ct = default);

	/// <summary>
	/// Take a memory back out of the active set — the narrow "this should not exist at all" escape hatch.
	/// Destructive and cascading (derived memories go with it); idempotent; hidden, never physically deleted.
	/// Prefer superseding via <see cref="RetainAsync"/> when there is a corrected replacement.
	/// </summary>
	ValueTask<RetractResult> RetractAsync(
		MemoryId memoryId, string? reason = null, CancellationToken ct = default);

	/// <summary>
	/// Search memory by meaning, ranked best-first. Reading, but not side-effect-free: a recall refreshes the
	/// recency clock of every memory it returns (reconsolidation). Retracted and superseded never surface.
	/// </summary>
	ValueTask<RecallResult> RecallAsync(
		string query, RecallOptions? options = null, CancellationToken ct = default);

	/// <summary>
	/// Fetch exact memories by id — progressive disclosure, unranked. Returns the requested records whatever
	/// their status (including retracted or superseded); ids that don't exist are simply absent from the stream.
	/// </summary>
	IAsyncEnumerable<StoredMemory> ReclaimAsync(
		IReadOnlyList<MemoryId> ids, CancellationToken ct = default);

	/// <summary>
	/// List memories by structure (type/tags), ordered by the requested sort, with no relevance ranking.
	/// Read-only. Retracted are excluded unless <see cref="RecollectOptions.IncludeRetracted"/> is set.
	/// </summary>
	IAsyncEnumerable<StoredMemory> RecollectAsync(
		RecollectOptions options, CancellationToken ct = default);

	/// <summary>
	/// Synthesize higher-level derived memories over a theme, possibly superseding or retracting the ones it
	/// subsumes. Writes new memories and is destructive; long-running and expected to run asynchronously.
	/// Not idempotent.
	/// </summary>
	ValueTask<ReflectResult> ReflectAsync(
		string query, ReflectOptions? options = null, CancellationToken ct = default);
}
