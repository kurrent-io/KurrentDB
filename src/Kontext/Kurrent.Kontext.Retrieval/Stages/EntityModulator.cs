// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Pipelines;

namespace Kurrent.Kontext.Retrieval;

/// <summary>
/// Nudges the pool's running relevance by what the question's own entities point at:
/// <para>final = base × (1 + α·signal), clamped to [min, max]</para>
/// <para>- rarity(n) = 1/(1 + k·(n−1)²) over an entity's mention counter. A name seen once is a
/// sharp clue (1.00), a name on everything ("the user") is no clue at all (~0).</para>
/// <para>- a NOTE is a written-down doubt between two entities the write side refused to merge.
/// Crossing it reaches the neighbour's memories at rarity × note confidence × penalty — ONE hop,
/// never two: a neighbour's own notes are not followed, so an unresolved doubt buys a reduced
/// push, never a widening walk.</para>
/// <para>- signal = 1 − Π(1−wᵢ) over every entity that reached a memory: independent clues compound
/// with diminishing returns and can never leave [0,1].</para>
/// <para>ZERO is the neutral, not the midpoint. Entity overlap is evidence FOR relevance, so no
/// overlap is absence of evidence, never evidence against — nothing this stage touches lands below
/// ×1.00. A memory no entity reached multiplies by exactly 1, so the relative order among unreached
/// memories is untouched, and a memory whose only tie is an ultra-common entity earns a push of ~0
/// rather than a penalty: matching on noise is worth nothing, not less than nothing.</para>
/// <para>The whole reachable range is therefore a push, small on purpose: a rare entity the question
/// names outright is worth the full +10%, and one hop across a 0.84 note is worth +4.2% — the
/// unresolved doubt still reaches you at a fraction of the strength, which is what makes deferring
/// the merge safe. The nudge wins ties at the bottom of the pool; it never overturns a clearly
/// better match.</para>
/// <para>Without an <see cref="IEntityIndex"/> the stage is a pass-through: the pool comes back as
/// it arrived, so a host with no entities read model ranks exactly as it did before entities
/// existed. Same for a question that names nothing known, and for entity matches that reach no
/// candidate in the pool.</para>
/// </summary>
public sealed class EntityModulator<TScale>(IEntityIndex? entities, EntityModulationOptions options) : IStep<Pool<TScale>, Pool<TScale>> where TScale : IScoreScale {
    // An entity this common cannot move any multiplier off 1, and its mention list is the longest
    // in the store by construction. Weighed, then dropped before the provenance walk.
    const double NegligibleWeight = 1e-4;

    /// <summary>Creates the stage from pre-built options — the config-binding door.</summary>
    public static EntityModulator<TScale> Create(IEntityIndex? entities, EntityModulationOptions options) =>
        new(entities, options);

    /// <summary>Creates the stage over default options, tuned via <paramref name="configure"/> when given.</summary>
    public static EntityModulator<TScale> Create(IEntityIndex? entities, Action<EntityModulationOptions>? configure = null) {
        var options = new EntityModulationOptions();
        configure?.Invoke(options);
        return Create(entities, options);
    }

    public async ValueTask<Pool<TScale>> Execute(Pool<TScale> input, CancellationToken ct) {
        var pool = input.Memories;

        if (entities is null || pool.Count == 0)
            return input;

        // The caller's question, not the planner's expanded text: expansion is a recall device for
        // the search legs, and a synonym the user never wrote must not name an entity for them.
        var weights = await WeighAsync(input.Query.Source.Text, ct).ConfigureAwait(false);

        if (weights.Count == 0)
            return input;

        var signals = await SignalAsync(weights, pool, ct).ConfigureAwait(false);

        if (signals.Count == 0)
            return input;

        IReadOnlyList<ScoredMemory> nudged = pool
            .Select(scored => signals.TryGetValue(scored.Memory.MemoryId, out var signal)
                ? scored with {
                    Score     = scored.Score * Multiplier(signal),
                    Breakdown = scored.Breakdown with { EntitySignal = signal },
                }
                : scored)
            .OrderByDescending(scored => scored.Score)
            .ThenBy(scored => scored.Memory.MemoryId, StringComparer.Ordinal)
            .ToList();

        return new(input.Query, nudged);
    }

    // Entity id → influence. Named entities carry their rarity; their note neighbours carry a
    // penalized share of it.
    async ValueTask<Dictionary<string, double>> WeighAsync(string question, CancellationToken ct) {
        var matches = await entities!.MatchAsync(question, ct).ConfigureAwait(false);

        if (matches.Count == 0)
            return [];

        var weights = new Dictionary<string, double>(StringComparer.Ordinal);

        foreach (var match in matches)
            weights[match.EntityId] = Math.Max(weights.GetValueOrDefault(match.EntityId), Rarity(match.MentionCount));

        var notes = await entities.ListNotesAsync([.. weights.Keys], ct).ConfigureAwait(false);

        if (notes.Count == 0)
            return weights;

        // Hops are weighed off a SNAPSHOT of the named entities, so a neighbour's own weight can
        // never seed another hop — that is what makes "one hop, never two" hold no matter what
        // shape the note graph has, including cycles.
        var named = new Dictionary<string, double>(weights, StringComparer.Ordinal);

        foreach (var note in notes) {
            Cross(note.SourceEntityId, note.TargetEntityId, note.Confidence);
            Cross(note.TargetEntityId, note.SourceEntityId, note.Confidence);
        }

        return weights;

        void Cross(string from, string to, double confidence) {
            if (from == to || !named.TryGetValue(from, out var rarity))
                return;

            var weight = rarity * confidence * options.NoteHopPenalty;

            weights[to] = Math.Max(weights.GetValueOrDefault(to), weight);
        }
    }

    // Memory id → 1 − Π(1−wᵢ), for pool candidates only. Provenance is append-only and positional,
    // so one entity mentioned twice in a memory is one clue, not two.
    async ValueTask<Dictionary<string, double>> SignalAsync(
        Dictionary<string, double> weights, IReadOnlyList<ScoredMemory> pool, CancellationToken ct
    ) {
        var influential = weights.Where(weight => weight.Value >= NegligibleWeight).Select(weight => weight.Key).ToList();

        if (influential.Count == 0)
            return [];

        var mentions = await entities!.ListMentionsAsync(influential, ct).ConfigureAwait(false);

        var candidates = pool.Select(scored => scored.Memory.MemoryId).ToHashSet(StringComparer.Ordinal);
        var counted    = new HashSet<(string Entity, string Memory)>();
        var missed     = new Dictionary<string, double>(StringComparer.Ordinal);

        foreach (var mention in mentions) {
            if (!candidates.Contains(mention.MemoryId) || !weights.TryGetValue(mention.EntityId, out var weight))
                continue;

            if (weight < NegligibleWeight || !counted.Add((mention.EntityId, mention.MemoryId)))
                continue;

            missed[mention.MemoryId] = missed.GetValueOrDefault(mention.MemoryId, 1.0) * (1 - weight);
        }

        return missed.ToDictionary(miss => miss.Key, miss => 1 - miss.Value, StringComparer.Ordinal);
    }

    // A counter of 0 (an entity taught by hand, or one whose last mention was swept) reads as
    // maximally rare rather than wrapping the curve past its peak.
    double Rarity(long mentionCount) {
        var excess = (double)(Math.Max(1, mentionCount) - 1);

        return 1.0 / (1.0 + options.RarityCurvature * excess * excess);
    }

    double Multiplier(double signal) =>
        Math.Clamp(1 + options.SignalAlpha * signal, options.MinMultiplier, options.MaxMultiplier);
}

/// <summary>
/// The entity-signal knobs: the rarity curve, what a note hop costs, and the push the nudge is
/// allowed to spend. Defaults are the design's own numbers, and they map the full signal range
/// [0,1] linearly onto [1.00, 1.10] — the clamp is a rail against a retuned gain, never a bound
/// the default configuration reaches from the inside.
/// </summary>
public sealed class EntityModulationOptions {
    /// <summary>Curvature k in rarity = 1/(1 + k·(n−1)²): 1 mention → 1.00, 30 → 0.54, 100 → 0.09, 2000 → 0.00.</summary>
    public double RarityCurvature { get; set; } = 0.001;

    /// <summary>What one note hop costs: the neighbour's weight is named rarity × note confidence × this.</summary>
    public double NoteHopPenalty { get; set; } = 0.5;

    /// <summary>Signal gain: a full signal is worth a push of +α, and no signal is worth nothing (0.1 → at most +10%).</summary>
    public double SignalAlpha { get; set; } = 0.1;

    /// <summary>Hard floor on the multiplier — 1.00, the rail behind "no overlap is absence of evidence, not evidence against".</summary>
    public double MinMultiplier { get; set; } = 1.00;

    /// <summary>Hard ceiling on the multiplier — the rail that keeps relevance king even if the gain is retuned up.</summary>
    public double MaxMultiplier { get; set; } = 1.10;
}
