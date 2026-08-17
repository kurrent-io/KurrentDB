// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Reflection;

namespace Kurrent.Kontext.Retrieval.Tests.Stages;

[Category("Stages")]
public class StageInvariantTests {
	public static IEnumerable<Func<StageCase>> Stages() {
		yield return () => StageCase.Of(CognitiveModulator<NativeScale>.Create());
		yield return () => StageCase.Of(BoostedModulator<NativeScale>.Create());
		yield return () => StageCase.Of(MmrReorderer<NativeScale>.Create());
		yield return () => StageCase.Of(RelevanceModelReranker<NativeScale>.Create(new FakeRelevanceModel(content => content.Length / 100.0)));
		yield return () => StageCase.Of(Bm25Reranker<NativeScale>.Create());
		yield return () => StageCase.Of(EntityModulator<NativeScale>.Create(EntitiesNamedByTheDefaultQuery()));
		yield return () => StageCase.Of(SeatAllocator<NativeScale>.Create(CapsTheInvariantPoolsFillExactly()));
	}

	// The invariant pools run under Fixtures.Query()'s "query" text, so the fake files an entity
	// under exactly that surface — the nudge has to actually fire for the invariants to mean anything.
	static FakeEntityIndex EntitiesNamedByTheDefaultQuery() =>
		new FakeEntityIndex()
			.Named("query", "ent-a")
			.Note("ent-a", "ent-b", 0.84)
			.Mentions("ent-a", "a")
			.Mentions("ent-b", "b");

	// The invariant pools run under Fixtures.Query()'s limit of 10, so 0.1 buys each capped kind
	// exactly one seat — the quota is consumed to its ceiling rather than merely computed, and the
	// pool still comes back whole because Pool() holds one candidate of each capped kind.
	static SeatAllocationOptions CapsTheInvariantPoolsFillExactly() =>
		new() {
			MaxShares = {
				[Contracts.MemoryType.Observation] = 0.1,
				[Contracts.MemoryType.Hearsay]     = 0.1,
			},
		};

	static readonly IReadOnlyDictionary<Type, string> ExcludedStages = new Dictionary<Type, string> {
		[typeof(RankFusionReranker<>)] =
			"Prunes to the union of the two top-PoolSize legs, so preserves_pool_membership cannot hold by design. Covered by RankFusionRerankerTests.",
	};

	static IReadOnlyList<ScoredMemory> Pool() => [
		Fixtures.Scored("a", 0.9, "aardvarks burrow deep underground", Contracts.MemoryType.Observation, Contracts.MemoryImportance.High, TimeSpan.FromDays(1)),
		Fixtures.Scored("b", 0.6, "penguins waddle across antarctic ice", Contracts.MemoryType.Fact, Contracts.MemoryImportance.Normal, TimeSpan.FromDays(10)),
		Fixtures.Scored("c", 0.3, "giraffes browse the tallest acacia leaves", Contracts.MemoryType.Hearsay, Contracts.MemoryImportance.Low, TimeSpan.FromDays(30)),
	];

	static IReadOnlyList<ScoredMemory> FusedPool() => [
		Fixtures.ScoredFrom("a", 0.9, "aardvarks burrow deep underground", (RetrievalSources.Vector, 1, 0.9), (RetrievalSources.Keyword, 3, 11.0)),
		Fixtures.ScoredFrom("b", 0.6, "penguins waddle across antarctic ice", (RetrievalSources.Keyword, 1, 18.0)),
		Fixtures.ScoredFrom("c", 0.3, "giraffes browse the tallest acacia leaves", (RetrievalSources.Vector, 2, 0.3)),
	];

	[Test, MethodDataSource(nameof(Stages))]
	public async ValueTask empty_pool_passes_through(StageCase stage) {
		var result = await stage.Run([]);

		await Assert.That(result).IsEmpty();
	}

	[Test, MethodDataSource(nameof(Stages))]
	public async ValueTask preserves_pool_membership(StageCase stage) {
		var result = await stage.Run(Pool());

		await Assert.That(Fixtures.Ids(result).Order().ToList()).IsEquivalentTo(["a", "b", "c"], CollectionOrdering.Matching);
	}

	[Test, MethodDataSource(nameof(Stages))]
	public async ValueTask is_deterministic(StageCase stage) {
		var first  = await stage.Run(Pool());
		var second = await stage.Run(Pool());

		await Assert.That(first.Select(scored => (scored.Memory.MemoryId, scored.Score)).ToList())
			.IsEquivalentTo(second.Select(scored => (scored.Memory.MemoryId, scored.Score)).ToList(), CollectionOrdering.Matching);
	}

	[Test, MethodDataSource(nameof(Stages))]
	public async ValueTask preserves_fusion_provenance(StageCase stage) {
		var pool   = FusedPool();
		var result = await stage.Run(pool);

		foreach (var scored in result) {
			var incoming = Incoming(pool, scored);

			await Assert.That(scored.Breakdown.Fused).IsEqualTo(incoming.Breakdown.Fused).Within(1e-12);
			await Assert.That(scored.Breakdown.SourceRanks.Count).IsEqualTo(incoming.Breakdown.SourceRanks.Count);
			await Assert.That(scored.Breakdown.SourceScores.Count).IsEqualTo(incoming.Breakdown.SourceScores.Count);

			foreach (var (source, rank) in incoming.Breakdown.SourceRanks)
				await Assert.That(scored.Breakdown.SourceRanks[source]).IsEqualTo(rank);

			foreach (var (source, score) in incoming.Breakdown.SourceScores)
				await Assert.That(scored.Breakdown.SourceScores[source]).IsEqualTo(score).Within(1e-12);
		}
	}

	[Test, MethodDataSource(nameof(Stages))]
	public async ValueTask never_nulls_a_populated_breakdown_field(StageCase stage) {
		var fields = NullableBreakdownFields();
		var pool   = FusedPool().Select(FullyPopulated).ToList();

		await Assert.That(fields.Where(field => field.GetValue(pool[0].Breakdown) is null).Select(field => field.Name).Order().ToList()).IsEmpty();

		var result = await stage.Run(pool);
		var nulled = new List<string>();

		foreach (var scored in result) {
			var incoming = Incoming(pool, scored);

			nulled.AddRange(fields
				.Where(field => field.GetValue(incoming.Breakdown) is not null && field.GetValue(scored.Breakdown) is null)
				.Select(field => $"{scored.Memory.MemoryId}.{field.Name}"));
		}

		await Assert.That(nulled).IsEmpty();
	}

	[Test, MethodDataSource(nameof(Stages))]
	public async ValueTask single_member_pool_keeps_its_member_and_a_finite_score(StageCase stage) {
		var result = await stage.Run([Fixtures.ScoredFrom("only", 0.42, "a lone candidate")]);

		await Assert.That(Fixtures.Ids(result)).IsEquivalentTo(["only"], CollectionOrdering.Matching);
		await Assert.That(double.IsFinite(result[0].Score)).IsTrue();
	}

	[Test]
	public async ValueTask every_stage_is_covered_or_consciously_excluded() {
		var classified = Stages()
			.Select(factory => factory().OpenType)
			.Concat(ExcludedStages.Keys)
			.ToHashSet();

		var unclassified = ConcreteStages()
			.Where(type => !classified.Contains(type))
			.Select(type => type.Name)
			.Order()
			.ToList();

		await Assert.That(unclassified).IsEmpty();
	}

	[Test]
	public async ValueTask exclusion_list_names_only_live_stages() {
		var stages = ConcreteStages().ToHashSet();

		var stale = ExcludedStages.Keys
			.Where(type => !stages.Contains(type))
			.Select(type => type.Name)
			.Order()
			.ToList();

		await Assert.That(stale).IsEmpty();
	}

	// A stage is a public step whose input AND output are pools — the plan/search/fuse/cut steps
	// carry other states and answer to their own suites.
	static IEnumerable<Type> ConcreteStages() =>
		typeof(IScoreScale).Assembly
			.GetTypes()
			.Where(type => type is { IsClass: true, IsAbstract: false, IsPublic: true } && type.GetInterfaces().Any(IsPoolStep));

	static bool IsPoolStep(Type contract) =>
		contract.IsGenericType
	 && contract.GetGenericTypeDefinition() == typeof(IStep<,>)
	 && contract.GetGenericArguments().All(static arg => arg.IsGenericType && arg.GetGenericTypeDefinition() == typeof(Pool<>));

	static IReadOnlyList<PropertyInfo> NullableBreakdownFields() =>
		typeof(ScoreBreakdown)
			.GetProperties()
			.Where(property => property.PropertyType == typeof(double?))
			.ToList();

	static ScoredMemory FullyPopulated(ScoredMemory scored) =>
		scored with {
			Breakdown = scored.Breakdown with {
				Reranked       = 0.11,
				RelevanceRaw   = 0.22,
				RelevanceNorm  = 0.33,
				RecencyRaw     = 0.44,
				RecencyNorm    = 0.55,
				ImportanceRaw  = 0.66,
				ImportanceNorm = 0.77,
				Certainty      = 0.88,
				BaseScore      = 0.99,
				ReorderScore   = 1.11,
				EntitySignal   = 0.42,
			},
		};

	static ScoredMemory Incoming(IEnumerable<ScoredMemory> pool, ScoredMemory scored) =>
		pool.Single(candidate => candidate.Memory.MemoryId == scored.Memory.MemoryId);
}

/// <summary>One stage under invariant test: its open generic type for the coverage sweep, and a runner over a bare pool.</summary>
public sealed record StageCase(string Name, Type OpenType, Func<IReadOnlyList<ScoredMemory>, ValueTask<IReadOnlyList<ScoredMemory>>> Run) {
	public static StageCase Of<TOut>(IStep<Pool<NativeScale>, Pool<TOut>> stage) where TOut : IScoreScale {
		var open = stage.GetType().GetGenericTypeDefinition();

		return new(open.Name[..open.Name.IndexOf('`')], open, pool => stage.Run(pool));
	}

	public override string ToString() => Name;
}
