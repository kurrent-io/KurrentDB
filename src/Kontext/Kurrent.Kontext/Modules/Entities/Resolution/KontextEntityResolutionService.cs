// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Data;
using Kurrent.Kontext.Modules.Entities.Data;

namespace Kurrent.Kontext.Modules.Entities.Resolution;

/// <summary>
/// One queued doubt with both its entries resolved, plus the survivor the default rule would pick
/// — everything a reviewer needs to rule without a second round trip. An entry can be missing when
/// cleanup swept it or a half-applied merge already folded it away.
/// </summary>
public sealed record PendingEntityLink {
	public required EntityLinkRow Link { get; init; }

	public EntityRow? Source { get; init; }
	public EntityRow? Target { get; init; }

	/// <summary>Which entry survives if the reviewer rules "same" without naming one; empty when neither entry is left.</summary>
	public string DefaultSurvivorEntityId { get; init; } = "";
}

/// <summary>
/// The human-correction entry point for the entities read model: the review queue's list surface
/// and the verdict that empties it. This is resolution (02 in the pipeline) — deliberately NOT a
/// stage of the write path, which is only allowed to make cheap, certain identity calls and files
/// a doubt for everything else.
///
/// Both operations run as a turn on the projector's write connection through
/// <see cref="EntityWriteGate"/>, reads included: a reviewer is about to make the one irreversible
/// move in the system, and the gate's connection is the only surface guaranteed to show every
/// batch already applied (a pooled connection serves the dataset view it FIRST scanned, so it can
/// silently miss recent writes — see <see cref="KontextEntityStore"/>). The cost is that both
/// operations require a running projector; without one the entity read model has no write surface
/// at all, and the gate says so.
///
/// No judge and no auto-confirm sweep here on purpose: this surface applies a verdict someone else
/// reached — a human, or later a smarter judge with the accumulated evidence the write path never had.
/// </summary>
public sealed class KontextEntityResolutionService(EntityWriteGate gate) {
	/// <summary>The filed doubts awaiting review, oldest first — the to-do list, exactly as the ledger orders it.</summary>
	public Task<List<PendingEntityLink>> ListPendingAsync(int limit = 50, CancellationToken ct = default) {
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

		return gate.RunAsync(
			async (connection, token) => {
				var store = new KontextEntityStore(connection);
				var links = await store.ListLinksAsync(EntityLinkStatus.Pending, limit, token).ConfigureAwait(false);

				var pending = new List<PendingEntityLink>(links.Count);

				foreach (var link in links) {
					var source = await store.GetAsync(link.SourceEntityId, token).ConfigureAwait(false);
					var target = await store.GetAsync(link.TargetEntityId, token).ConfigureAwait(false);

					pending.Add(new() {
						Link                    = link,
						Source                  = source,
						Target                  = target,
						DefaultSurvivorEntityId = EntityVerdictExecutor.PreviewSurvivor(source, target),
					});
				}

				return pending;
			}, ct);
	}

	/// <summary>
	/// Rules on one doubt. <see cref="EntityLinkVerdict.SameEntity"/> merges — one entry survives,
	/// the other's spellings and mentions fold onto it and its row goes — and
	/// <see cref="EntityLinkVerdict.DifferentEntities"/> records the decision so the doubt stops
	/// costing anything at read time. <paramref name="survivorEntityId"/> is the reviewer's override
	/// of survivor selection and must name one of the endpoints. Re-ruling a decided link changes
	/// nothing.
	/// </summary>
	public Task<EntityLinkResolution> ResolveAsync(
		string sourceEntityId,
		string targetEntityId,
		EntityLinkVerdict verdict,
		string? survivorEntityId = null,
		CancellationToken ct = default
	) =>
		gate.RunAsync(
			(connection, token) => new EntityVerdictExecutor(connection, KontextSchemaTask.Dimension)
				.ApplyAsync(sourceEntityId, targetEntityId, verdict, survivorEntityId, token),
			ct);
}
