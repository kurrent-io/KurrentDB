// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text;
using Kurrent.Kontext.Data;
using Kurrent.Kontext.Embeddings.SentencePieceOnnx;
using Kurrent.Kontext.Memory.Data;
using Kurrent.Kontext.Retrieval;
using Kurrent.Kontext.Testing;
using Microsoft.Extensions.AI;
using MemoryContracts = Kurrent.Kontext.Contracts.V3.Memory;

namespace Kurrent.Kontext.Tests.Data;

/// <summary>
/// PROBE: how should retain look for the memory it is about to duplicate — one hybrid call, or a
/// vector call and a keyword call?
///
/// `_hybrid_score` cannot answer MERGE / APPEND / AMBIGUOUS: lance-duckdb min-max normalises each
/// leg across the returned pool and adds NOTHING for a leg that missed the row
/// (rust/ffi/search.rs:434-442), so a row found by one leg alone lands on exactly alpha whatever
/// its true similarity. `_distance` is written raw (search.rs:462-470) and the vectors are unit
/// length, so it is the only pool-independent number available.
///
/// That leaves the real question. lance_hybrid_search truncates to k by the fused score
/// (search.rs:446-447), so a vector-only match competes for its slot against keyword-only
/// strangers holding the identical 0.5. This measures whether that truncation drops rows the
/// vector leg alone would have returned.
/// </summary>
[Category("Integration")]
[Timeout(600_000)]
public class DuplicateDistanceSeparationProbeTests {
	const int NoiseSize = 300;
	const int K         = 50;

	const string Marker = "DUP-SEP";

	static readonly DateTimeOffset Base = new(2026, 7, 1, 10, 0, 0, TimeSpan.Zero);

	// (stored twin, incoming memory). Lifted verbatim from RelatedPipelineTuningProbeTests so the
	// two probes measure the same planted pairs from different angles.
	static readonly (string Stored, string Incoming)[] Lexical = [
		("the test runner lives at scripts/testing/test-runner.cs",      "tests run only through scripts/testing/test-runner.cs"),
		("the projector checkpoints after the batch lands",              "the projector checkpoints once the batch has landed"),
		("KontextMemoryWriter batches every statement into one command", "KontextMemoryWriter puts every statement in a single command"),
		("the memories table stores log_position with a BTREE index",    "log_position on the memories table carries a BTREE index"),
		("recall embeds content and nothing else",                       "only content is embedded by recall"),
		("retain mints every memory id on the server",                   "the server mints each memory id during retain"),
	];

	static readonly (string Stored, string Incoming)[] Semantic = [
		("the feline rested upon the rug",                              "a cat sat on a mat"),
		("the build broke after the dependency bump",                   "CI went red once the package version changed"),
		("we abandoned the second index because it cost too much disk", "the extra lookup structure was dropped over storage overhead"),
		("the writer never mutates rows the projector owns",            "only the projection process changes those records"),
		("a colleague reported the outage during standup",              "someone mentioned the downtime at the morning meeting"),
		("the schema was reset rather than migrated",                   "instead of upgrading, the tables were rebuilt from scratch"),
	];

	// Incoming memories with NO twin in the corpus. Retain must classify these APPEND, so their
	// nearest neighbour is the distance a stranger actually produces — the other half of the
	// boundary question, which planted pairs alone cannot answer.
	static readonly string[] Strangers = [
		"the certificate rotation job runs every ninety days",
		"Sérgio prefers commit messages in the imperative mood",
		"gossip timeouts default to two seconds in cluster mode",
		"the janitor skips tables whose row count did not move",
		"OpenTelemetry traces are exported over OTLP gRPC",
		"the admin UI listens on the same port as the gRPC surface",
	];

	[Test]
	public async ValueTask compares_hybrid_against_separate_legs_for_finding_the_memory_about_to_be_duplicated(CancellationToken cancellationToken) {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = MemorySeeding.NewDataSources(dir.Path);
		using var embeddings  = new Pmm12EmbeddingGenerator();

		var options = new EmbeddingGenerationOptions { Dimensions = KontextIndexConstants.VectorsDimension };

		var pairs = Lexical.Select(p => (p.Stored, p.Incoming, Kind: "lexical"))
			.Concat(Semantic.Select(p => (p.Stored, p.Incoming, Kind: "semantic")))
			.ToArray();

		// Real conversational prose as noise. Synthetic filler that repeats one sentence leaves the
		// keyword leg with almost no vocabulary to discriminate on, which flatters BM25's apparent
		// precision and makes every FTS number meaningless.
		var noise = await LoadNoise(cancellationToken);

		var stored = pairs.Select(pair => pair.Stored).Concat(noise).ToArray();

		var storedVectors = await embeddings.GenerateAsync(stored, options, cancellationToken);
		var tag           = KontextMemoryDataStore.EncodeTag(new MemoryContracts.Tag { Scope = "user", Value = "sergio" });

		var rows = stored
			.Select((content, i) => new MemoryRow($"m{i}", MemoryContracts.MemoryType.Fact, content, MemoryContracts.MemoryImportance.Normal, Base) {
				Embedding = storedVectors[i].Vector.ToArray(),
				Tags      = [tag],
			})
			.ToArray();

		var store = await MemorySeeding.Seed(dataSources, rows);

		// The schema creates content_fts on the empty table, so every seeded row lands in the
		// unindexed tail — where lance_fts returns the first k rows by scan arrival rather than the
		// top k by score. Without this rebuild the keyword leg measures nothing. See KontextCorpus.
		RebuildContentFts(dataSources);

		MemoryContracts.Tag[] scope = [new() { Scope = "user", Value = "sergio" }];

		// Unit length is what makes a raw distance mean the same thing in every query: squared L2
		// reduces to 2-2cos only on the unit sphere.
		var norms = storedVectors.Select(v => Norm(v.Vector.Span)).ToArray();

		var probes = pairs.Select(p => (Query: p.Incoming, Twin: p.Stored, p.Kind))
			.Concat(Strangers.Select(s => (Query: s, Twin: (string)null!, Kind: "stranger")))
			.ToArray();

		var queryVectors = await embeddings.GenerateAsync(probes.Select(p => p.Query).ToArray(), options, cancellationToken);

		// Act — the same probe down three routes at the same page size.
		var measured = new List<Measurement>();

		for (var i = 0; i < probes.Length; i++) {
			var probe  = probes[i];
			var vector = queryVectors[i].Vector.ToArray();

			var hybrid    = await Collect(store.SearchAsync(probe.Query, vector, scope, new HybridSearchOptions { K = K, Alpha = 0.5 }, cancellationToken));
			var vectorOnly = await Collect(store.SearchAsync(vector, scope, new VectorSearchOptions { K = K }, cancellationToken));
			var ftsOnly    = await Collect(store.SearchAsync(probe.Query, scope, new FullTextSearchOptions { K = K }, cancellationToken));

			// The closest row that is NOT the twin. For a stranger probe every row qualifies, so
			// this is its nearest neighbour — the distance retain would have to reject.
			var nearestOther = vectorOnly
				.Where(h => h.Memory.Content != probe.Twin && h.VectorDistance is not null)
				.Min(h => h.VectorDistance!.Value);

			measured.Add(new(
				probe.Kind,
				probe.Query,
				HybridDistance: Twin(hybrid, probe.Twin)?.VectorDistance,
				HybridFound: Twin(hybrid, probe.Twin) is not null,
				VectorDistance: Twin(vectorOnly, probe.Twin)?.VectorDistance,
				VectorFound: Twin(vectorOnly, probe.Twin) is not null,
				KeywordFound: Twin(ftsOnly, probe.Twin) is not null,
				NearestOtherDistance: nearestOther));
		}

		var report = new StringBuilder();

		report.AppendLine();
		report.AppendLine($"{Marker} corpus={stored.Length}  noise=locomo  k={K}  dim={KontextIndexConstants.VectorsDimension}");
		report.AppendLine($"{Marker} vector L2 norm  min={norms.Min():F6}  max={norms.Max():F6}  spread={norms.Max() - norms.Min():E3}");
		report.AppendLine();
		report.AppendLine($"{Marker} twin found at k={K}, by route (n = 6 per kind)");
		report.AppendLine($"{Marker} | kind     | hybrid | vector | keyword |");
		report.AppendLine($"{Marker} |----------|--------|--------|---------|");

		foreach (var kind in (string[])["lexical", "semantic"]) {
			var group = measured.Where(m => m.Kind == kind).ToArray();

			report.AppendLine(
				$"{Marker} | {kind,-8} | {group.Count(m => m.HybridFound),6} | " +
				$"{group.Count(m => m.VectorFound),6} | {group.Count(m => m.KeywordFound),7} |");
		}

		report.AppendLine();
		report.AppendLine($"{Marker} vector-leg distances");
		report.AppendLine($"{Marker} | kind     | twin _distance  | nearest other   |");
		report.AppendLine($"{Marker} |----------|-----------------|-----------------|");

		foreach (var kind in (string[])["lexical", "semantic", "stranger"]) {
			var group = measured.Where(m => m.Kind == kind).ToArray();
			var twins = group.Where(m => m.VectorDistance is not null).Select(m => m.VectorDistance!.Value).ToArray();

			var twinCell = twins.Length == 0 ? "      n/a      " : $"{twins.Min():F4} - {twins.Max():F4}";

			report.AppendLine(
				$"{Marker} | {kind,-8} | {twinCell} | " +
				$"{group.Min(m => m.NearestOtherDistance):F4} - {group.Max(m => m.NearestOtherDistance):F4} |");
		}

		// A twin only hybrid loses is a row the fused truncation discarded — the cost of the single
		// call, stated in rows rather than in theory.
		foreach (var lost in measured.Where(m => m.Kind != "stranger" && m.VectorFound && !m.HybridFound))
			report.AppendLine($"{Marker} LOST BY HYBRID, kept by vector ({lost.Kind}): {lost.Query}");

		foreach (var miss in measured.Where(m => m.Kind != "stranger" && !m.VectorFound))
			report.AppendLine($"{Marker} UNREACHABLE — no route found it ({miss.Kind}): {miss.Query}");

		var worstDuplicate  = measured.Where(m => m.VectorDistance is not null).Max(m => m.VectorDistance!.Value);
		var closestNonMerge = measured.Where(m => m.Kind == "stranger").Min(m => m.NearestOtherDistance);
		var lexicalBand     = measured.Where(m => m.Kind == "lexical" && m.VectorDistance is not null).Max(m => m.VectorDistance!.Value);

		report.AppendLine();
		report.AppendLine($"{Marker} lexical band ceiling        {lexicalBand:F4}");
		report.AppendLine($"{Marker} worst duplicate distance    {worstDuplicate:F4}");
		report.AppendLine($"{Marker} closest stranger distance   {closestNonMerge:F4}");
		report.AppendLine($"{Marker} one threshold separates all {(worstDuplicate < closestNonMerge ? "YES" : "NO")}  gap={closestNonMerge - worstDuplicate:F4}");

		Console.WriteLine(report.ToString());

		// Assert
		foreach (var norm in norms)
			await Assert.That(norm).IsEqualTo(1.0).Within(1e-3);

		// A lexical restatement is the case dedup must never miss: the same claim in mostly the same
		// words. A twin that is not retrieved, or that fails to outrank every other row, is
		// unreachable by any threshold placed downstream of the search.
		foreach (var entry in measured.Where(m => m.Kind == "lexical")) {
			await Assert.That(entry.VectorDistance).IsNotNull();
			await Assert.That(entry.VectorDistance!.Value).IsLessThan(entry.NearestOtherDistance);
		}

		// The safety property a MERGE threshold rests on: the band holding every lexical twin must
		// hold no stranger, or retain would supersede an unrelated memory.
		await Assert.That(lexicalBand).IsLessThan(closestNonMerge);
	}

	readonly record struct Measurement(
		string  Kind,
		string  Query,
		double? HybridDistance,
		bool    HybridFound,
		double? VectorDistance,
		bool    VectorFound,
		bool    KeywordFound,
		double  NearestOtherDistance);

	static MemoryHit? Twin(List<MemoryHit> hits, string? twin) =>
		twin is null ? null : hits.Where(h => h.Memory.Content == twin).Cast<MemoryHit?>().FirstOrDefault();

	static async ValueTask<List<MemoryHit>> Collect(IAsyncEnumerable<MemoryHit> hits) {
		var collected = new List<MemoryHit>();

		await foreach (var hit in hits)
			collected.Add(hit);

		return collected;
	}

	static async ValueTask<string[]> LoadNoise(CancellationToken ct) {
		var corpus = await CorpusFixture.Load(Path.Combine(AppContext.BaseDirectory, "Corpus", "Data", "locomo-conv26.json"));

		return corpus.Memories
			.Select(memory => memory.Content)
			.Where(content => content.Length >= 40)
			.Distinct()
			.Take(NoiseSize)
			.ToArray();
	}

	static void RebuildContentFts(KontextDataSource dataSources) =>
		dataSources.Execute(connection => {
			using var command = connection.CreateCommand();
			command.CommandText =
				"""
				CREATE INDEX content_fts ON ldb.main.memories (content) USING INVERTED
				WITH (replace = true, base_tokenizer = 'simple', language = 'English', stem = true);
				""";
			command.ExecuteNonQuery();
		});

	static double Norm(ReadOnlySpan<float> vector) {
		var sum = 0d;

		foreach (var value in vector)
			sum += (double)value * value;

		return Math.Sqrt(sum);
	}
}
