// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Pipelines;

namespace Kurrent.Kontext.Retrieval;

/// <summary>
/// Reserves the final N seats by kind, so one loud kind cannot fill the whole answer:
/// <para>seats(kind) = floor(share·N) for a kind the host capped; a kind it never capped is uncapped.</para>
/// <para>Candidates rank WITHIN their kind by the pool's running order — that order arrived earned
/// (fusion, reread, modulation, MMR) and this stage never re-sorts it, across kinds or within one.
/// It only drops what a capped kind has no seat left for, so the pool comes back as it arrived,
/// minus the overflow.</para>
/// <para>The asymmetry is the whole point: spare seats go to UNCAPPED kinds in running order, and a
/// capped kind that reached its ceiling never re-enters through them. With only capped candidates
/// left over the answer comes back SHORT of N — a ceiling leftovers could refill is no ceiling.</para>
/// <para>No configured cap is a pass-through: the pool comes back untouched, so a host that
/// configures nothing ranks and cuts exactly as it did before seats existed.</para>
/// <para>Sits immediately before <see cref="CutStep{TScale}"/> rather than replacing it. Which
/// candidates a kind may seat is a ranking decision, so it stays a pool-to-pool stage answerable to
/// the same invariants as every other stage, and the caller's limit and score floor stay read in the
/// one step typed to the chain's final scale. The limit is read here only to size the quotas.</para>
/// </summary>
public sealed class SeatAllocator<TScale>(SeatAllocationOptions options) : IStep<Pool<TScale>, Pool<TScale>> where TScale : IScoreScale {
    /// <summary>Creates the stage from pre-built options — the config-binding door.</summary>
    public static SeatAllocator<TScale> Create(SeatAllocationOptions options) =>
        new(options);

    /// <summary>Creates the stage over default options, tuned via <paramref name="configure"/> when given.</summary>
    public static SeatAllocator<TScale> Create(Action<SeatAllocationOptions>? configure = null) {
        var options = new SeatAllocationOptions();
        configure?.Invoke(options);
        return Create(options);
    }

    public ValueTask<Pool<TScale>> Execute(Pool<TScale> input, CancellationToken ct) {
        var pool = input.Memories;

        if (pool.Count == 0 || options.MaxShares.Count == 0)
            return new(input);

        var limit  = input.Query.Source.Limit;
        var left   = new Dictionary<Contracts.MemoryType, int>();
        var seated = new List<ScoredMemory>(pool.Count);

        foreach (var scored in pool) {
            var kind = scored.Memory.MemoryType;

            if (!options.MaxShares.TryGetValue(kind, out var share)) {
                seated.Add(scored);
                continue;
            }

            if (!left.TryGetValue(kind, out var seats))
                seats = Seats(share, limit);

            if (seats <= 0)
                continue;

            left[kind] = seats - 1;
            seated.Add(scored);
        }

        return new(new Pool<TScale>(input.Query, seated));
    }

    // floor(share·N), the share clamped to [0,1]: a negative share seats nobody, and one past 1
    // cannot buy a kind more seats than the cut will take anyway.
    static int Seats(double share, int limit) =>
        (int)Math.Floor(Math.Clamp(share, 0, 1) * limit);
}

/// <summary>
/// The seat policy: a ceiling on what share of the answer a memory kind may hold. Empty by default,
/// so every shipped chain cuts as a plain top-N until a host caps something.
/// </summary>
public sealed class SeatAllocationOptions {
    /// <summary>
    /// Per-kind ceiling as a fraction of the caller's limit — OBSERVATION → 0.5 caps chat at half
    /// the seats. A kind absent from the map is uncapped, and uncapped kinds are the only ones spare
    /// seats can reach.
    /// </summary>
    public Dictionary<Contracts.MemoryType, double> MaxShares { get; set; } = new();
}
