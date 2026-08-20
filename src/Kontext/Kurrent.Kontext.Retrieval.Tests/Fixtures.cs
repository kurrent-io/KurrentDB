// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Google.Protobuf.WellKnownTypes;
using MemoryContracts = Kurrent.Kontext.Contracts.V3.Memory;

namespace Kurrent.Kontext.Retrieval.Tests;

static class Fixtures {
	public static readonly DateTimeOffset Now = new(2026, 7, 1, 10, 0, 0, TimeSpan.Zero);

	public static MemoryContracts.StoredMemory Memory(
		string id,
		string content = "content",
		MemoryContracts.MemoryType type = MemoryContracts.MemoryType.Fact,
		MemoryContracts.MemoryImportance importance = MemoryContracts.MemoryImportance.Normal,
		TimeSpan age = default,
		params string[] cites
	) {
		var memory = new MemoryContracts.StoredMemory {
			MemoryId       = id,
			Content        = content,
			MemoryType     = type,
			Importance     = importance,
			LastAccessedAt = Timestamp.FromDateTimeOffset(Now - age),
		};

		foreach (var cited in cites)
			memory.Evidence.Add(new MemoryContracts.Evidence {
				Memory = new MemoryContracts.Evidence.Types.MemoryRef { Id = cited },
			});

		return memory;
	}

	public static SearchCandidate Candidate(string id, double score) =>
		new(Memory(id), score);

	public static ScoredMemory Scored(
		string id,
		double score,
		string content = "content",
		MemoryContracts.MemoryType type = MemoryContracts.MemoryType.Fact,
		MemoryContracts.MemoryImportance importance = MemoryContracts.MemoryImportance.Normal,
		TimeSpan age = default,
		IReadOnlyDictionary<string, int>? sourceRanks = null,
		IReadOnlyDictionary<string, double>? sourceScores = null,
		params string[] cites
	) => new() {
		Memory = Memory(id, content, type, importance, age, cites),
		Score  = score,
		Breakdown = new() {
			Fused        = score,
			SourceRanks  = sourceRanks ?? new Dictionary<string, int>(),
			SourceScores = sourceScores ?? new Dictionary<string, double>(),
		},
	};

	public static ScoredMemory ScoredFrom(
		string id,
		double score,
		string content = "content",
		params (string Source, int Rank, double Score)[] sources
	) {
		var provenance = sources.Length > 0
			? sources
			: new[] { (Source: RetrievalSources.Vector, Rank: 1, Score: score) };

		return Scored(
			id,
			score,
			content,
			sourceRanks: provenance.ToDictionary(source => source.Source, source => source.Rank),
			sourceScores: provenance.ToDictionary(source => source.Source, source => source.Score)
		);
	}

	public static PlannedQuery Query(string text = "query", int limit = 10) =>
		new() {
			Text     = text,
			Tags     = [],
			Limit    = limit,
			PoolSize = 60,
			AsOf     = Now,
		};

	public static IReadOnlyList<string> Ids(IEnumerable<ScoredMemory> pool) =>
		pool.Select(scored => scored.Memory.MemoryId).ToList();
}
