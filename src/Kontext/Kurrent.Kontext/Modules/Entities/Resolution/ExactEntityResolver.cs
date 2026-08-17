// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Modules.Entities.Data;

namespace Kurrent.Kontext.Modules.Entities.Resolution;

/// <summary>
/// Equality resolution: the stored entity of the probe's type whose normalized name or alias
/// equals the probe's normalized name. The cheapest strategy and the only one that can claim
/// certainty, so it always runs first in the composite chain.
/// </summary>
public sealed class ExactEntityResolver(KontextEntityStore store) : IEntityResolver {
	public async ValueTask<EntityResolution> ResolveAsync(ResolutionProbe probe, CancellationToken ct = default) {
		var name = probe.Probe.NormalizedName;

		// Batch pool first — an entity touched this batch is the freshest truth.
		foreach (var pending in probe.PendingOfType())
			if (pending.Row.NormalizedName == name || pending.Row.Aliases.Contains(name))
				return new() { Match = pending.Row, Score = 1.0, Method = ResolutionMethod.Exact };

		var match = await store.FindExactAsync(name, probe.Probe.EntityType, ct).ConfigureAwait(false);

		return match is null
			? EntityResolution.Unmatched
			: new() { Match = match, Score = 1.0, Method = ResolutionMethod.Exact };
	}
}
