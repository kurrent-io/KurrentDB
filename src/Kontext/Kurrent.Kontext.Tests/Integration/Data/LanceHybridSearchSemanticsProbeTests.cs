// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using DuckDB.NET.Data;
using Kurrent.Kontext.Data;
using Kurrent.Kontext.Embeddings.SentencePieceOnnx;
using Kurrent.Kontext.Infrastructure.Data;
using Kurrent.Kontext.Memory.Data;
using Kurrent.Kontext.Testing;
using Microsoft.Extensions.AI;
using MemoryContracts = Kurrent.Kontext.Contracts.V3.Memory;

namespace Kurrent.Kontext.Tests.Data;

/// <summary>
/// PROBE: what lance_hybrid_search actually guarantees, now that containment pushdown is fixed.
///
/// Four questions the store's SQL currently assumes answers to:
///  1. does the trailing SQL LIMIT change anything, or does k alone set the page?
///  2. is `WHERE is_superseded = false` pushed into the engine, or applied above its k rows?
///  3. at extreme superseded ratios, does a page still fill, or does it thin out?
///  4. what per-leg detail (_distance, _score) does the blend collapse and MemoryHit discard?
/// </summary>
[Category("Integration")]
[Timeout(600_000)]
public class LanceHybridSearchSemanticsProbeTests {
	const int CorpusSize = 200;
	const int K          = 10;

	const string Marker = "LANCE-SEMANTICS";

	static readonly DateTimeOffset Base = new(2026, 7, 1, 10, 0, 0, TimeSpan.Zero);

	[Test]
	public async ValueTask documents_k_limit_and_superseded_pushdown(CancellationToken cancellationToken) {
		using var embeddings = new Pmm12EmbeddingGenerator();

		var options = new EmbeddingGenerationOptions { Dimensions = KontextIndexConstants.VectorsDimension };
		var query   = "checkpoint format decision for the projector";
		var texts   = Enumerable.Range(0, CorpusSize).Select(i => $"checkpoint format decision for the projector, variant {i}").ToArray();
		var vectors = (await embeddings.GenerateAsync(texts, options, cancellationToken)).Select(v => v.Vector.ToArray()).ToArray();
		var qv      = (await embeddings.GenerateAsync([query], options, cancellationToken))[0].Vector.ToArray();

		// ---- 1. does the SQL LIMIT do anything once k is set? --------------------------------
		using (var dir = new TempDir()) {
			using var sources = MemorySeeding.NewDataSources(dir.Path);
			await Seed(sources, texts, vectors, liveWanted: CorpusSize);

			Console.WriteLine($"{Marker} == 1. LIMIT vs k (k={K}, no rows superseded) ==");
			Console.WriteLine($"{Marker} limitClause   rows");

			foreach (var limit in (int?[])[null, 1, 5, 10, 100]) {
				var rows = Count(sources, qv, query, K, limit, filterSuperseded: false);
				Console.WriteLine($"{Marker} {(limit is null ? "none" : limit.ToString()),11}   {rows,4}");
			}
		}

		// ---- 2 + 3. superseded pushdown across ratios ----------------------------------------
		Console.WriteLine($"{Marker} == 2/3. is_superseded pushdown (k={K}, corpus={CorpusSize}) ==");
		Console.WriteLine($"{Marker} superseded  live   noWhere  withWhere   verdict");

		foreach (var liveWanted in (int[])[200, 100, 20, 5, 2, 0]) {
			using var dir     = new TempDir();
			using var sources = MemorySeeding.NewDataSources(dir.Path);

			var supersededCount = await Seed(sources, texts, vectors, liveWanted);
			var live            = CorpusSize - supersededCount;

			var noWhere   = Count(sources, qv, query, K, limit: null, filterSuperseded: false);
			var withWhere = Count(sources, qv, query, K, limit: null, filterSuperseded: true);

			// Pushed down => the page still fills from live rows while enough exist.
			// Applied above => withWhere thins in proportion to the superseded share.
			var expectedIfPushed = Math.Min(K, live);
			var verdict = withWhere == expectedIfPushed ? "pushed-down"
				: withWhere < expectedIfPushed ? "POST-FILTERED" : "?";

			Console.WriteLine($"{Marker} {supersededCount,10}  {live,4}   {noWhere,7}  {withWhere,9}   {verdict}");
		}

		// ---- 4. what the blend collapses -----------------------------------------------------
		using (var dir = new TempDir()) {
			using var sources = MemorySeeding.NewDataSources(dir.Path);
			await Seed(sources, texts, vectors, liveWanted: CorpusSize);

			Console.WriteLine($"{Marker} == 4. per-leg columns the blend hides (top 5) ==");
			Console.WriteLine($"{Marker}  _distance    _score  _hybrid_score");

			foreach (var (distance, score, hybrid) in Legs(sources, qv, query, K))
				Console.WriteLine($"{Marker} {Fmt(distance),10}  {Fmt(score),8}  {Fmt(hybrid),13}");
		}

		await Assert.That(CorpusSize).IsGreaterThan(K);
	}

	static string Fmt(double? value) => value is null ? "NULL" : value.Value.ToString("F4");

	// Marks everything EXCEPT the first `liveWanted` rows as superseded, so the sweep reaches the
	// extremes: an earlier version keyed off a modulus and never got past 50%.
	static async ValueTask<int> Seed(KontextDataSource sources, string[] texts, float[][] vectors, int liveWanted) {
		var rows = texts
			.Select((content, i) => {
				var superseded = i >= liveWanted;

				return new MemoryRow($"m{i}", MemoryContracts.MemoryType.Fact, content, MemoryContracts.MemoryImportance.Normal, Base) {
					Embedding    = vectors[i],
					IsSuperseded = superseded,
					SupersededAt = superseded ? Base.AddHours(1) : null,
					SupersededBy = superseded ? "live" : "",
				};
			})
			.ToArray();

		await MemorySeeding.Seed(sources, rows);

		return rows.Count(row => row.IsSuperseded);
	}

	static int Count(KontextDataSource sources, float[] embedding, string query, int k, int? limit, bool filterSuperseded) =>
		sources.Execute(connection => {
			using var command = connection.CreateCommand();

			command.CommandText = Sql("memory_id", embedding.Length, filterSuperseded, limit);
			Bind(command, embedding, query, k);

			using var reader = command.ExecuteReader();

			var rows = 0;
			while (reader.Read()) rows++;

			return rows;
		});

	static List<(double? Distance, double? Score, double? Hybrid)> Legs(KontextDataSource sources, float[] embedding, string query, int k) =>
		sources.Execute(connection => {
			using var command = connection.CreateCommand();

			command.CommandText = Sql("_distance, _score, _hybrid_score", embedding.Length, filterSuperseded: false, limit: 5);
			Bind(command, embedding, query, k);

			using var reader = command.ExecuteReader();
			var       legs   = new List<(double?, double?, double?)>();

			while (reader.Read())
				legs.Add((
					reader.IsDBNull(0) ? null : Convert.ToDouble(reader.GetValue(0)),
					reader.IsDBNull(1) ? null : Convert.ToDouble(reader.GetValue(1)),
					reader.IsDBNull(2) ? null : Convert.ToDouble(reader.GetValue(2))));

			return legs;
		});

	static string Sql(string projection, int dimensions, bool filterSuperseded, int? limit) =>
		$"""
		 SELECT {projection}
		 FROM lance_hybrid_search('ldb.main.memories', 'embedding', CAST($query_embedding AS FLOAT[{dimensions}]),
		                          'content', $query,
		                          k := $k,
		                          prefilter := true,
		                          alpha := 0.5){(filterSuperseded ? "\nWHERE is_superseded = false" : "")}{(limit is { } value ? $"\nLIMIT {value}" : "")}
		 """;

	static void Bind(DuckDBCommand command, float[] embedding, string query, int k) {
		command.Parameters.Add(new DuckDBParameter("query_embedding", embedding));
		command.Parameters.Add(new DuckDBParameter("query", query));
		command.Parameters.Add(new DuckDBParameter("k", k));
	}
}
