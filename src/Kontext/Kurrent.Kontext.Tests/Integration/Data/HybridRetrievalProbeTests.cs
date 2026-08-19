// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Globalization;
using System.Text;
using Kurrent.Kontext.Data;
using Kurrent.Kontext.Embeddings.Normalization;
using Kurrent.Kontext.Embeddings.SentencePieceOnnx;
using Kurrent.Kontext.Infrastructure.Data;
using Kurrent.Kontext.Infrastructure.Data.LanceDB;
using Kurrent.Kontext.Testing;
using Kurrent.Quack;
using Microsoft.Extensions.AI;

namespace Kurrent.Kontext.Tests.Data;

/// <summary>
/// Retrieval quality of raw JSON against JsonNormalizer output over a corpus large enough to
/// train a vector index: two tables holding the same 400 records in the two representations,
/// scored by MRR across fts, vector and hybrid, with the ANN index on and off.
/// </summary>
[Category("Integration")]
[Timeout(600_000)]
public class HybridRetrievalProbeTests {
	const int  CorpusSize = 400;
	const int  K          = 20;
	const string Dimension = "384";

	[Test]
	public async ValueTask ranks_raw_json_against_normalized_with_and_without_the_vector_index(CancellationToken cancellationToken) {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = MemorySeeding.NewDataSources(dir.Path);
		using var connection  = dataSources.OpenLanceWriter();
		using var embeddings  = new Pmm12EmbeddingGenerator();

		var options = new EmbeddingGenerationOptions { Dimensions = KontextIndexConstants.VectorsDimension };
		var corpus  = BuildCorpus();

		var raw        = corpus.Select(record => record.Json).ToArray();
		var normalized = raw.Select(json => JsonNormalizer.Instance.Normalize(Encoding.UTF8.GetBytes(json))!).ToArray();

		var rawVectors        = await embeddings.GenerateAsync(raw, options, cancellationToken);
		var normalizedVectors = await embeddings.GenerateAsync(normalized, options, cancellationToken);

		var trained = new Dictionary<string, bool>();

		foreach (var (table, texts, vectors) in new[] {
			("corpus_raw", raw, rawVectors),
			("corpus_norm", normalized, normalizedVectors),
		}) {
			Exec(connection, $"CREATE TABLE ldb.main.{table} (id BIGINT, text VARCHAR, embedding FLOAT[{Dimension}])");
			Exec(connection, $"CREATE INDEX text_fts ON ldb.main.{table} (text) USING INVERTED WITH (\n{CodeOptions}\n)");

			for (var i = 0; i < texts.Length; i++)
				Exec(connection, $"INSERT INTO ldb.main.{table} VALUES ({i}, '{texts[i].Replace("'", "''")}', {Vector(vectors[i].Vector.Span)})");

			dataSources.Execute(c => c.EnsureInvertedIndex($"ldb.main.{table}", "text"));
			trained[table] = dataSources.Execute(c => c.EnsureVectorIndex($"ldb.main.{table}", "embedding", new LanceIvfPqIndexOptions { NumPartitions = 1, NumSubVectors = KontextIndexConstants.VectorsDimension / 8 }));
		}

		var queryVectors = await embeddings.GenerateAsync(Targets.Select(target => target.Question), options, cancellationToken);

		var report = new StringBuilder();

		report.AppendLine();
		report.AppendLine($"corpus {CorpusSize}   k {K}   vector index trained: raw={trained["corpus_raw"]} norm={trained["corpus_norm"]}");
		report.AppendLine();
		report.AppendLine("| leg | raw json MRR | normalized MRR |");
		report.AppendLine("|---|---|---|");

		// Act
		foreach (var (leg, sql) in Legs())
			report.AppendLine($"| {leg} | {Mrr("corpus_raw", sql):F4} | {Mrr("corpus_norm", sql):F4} |");

		Console.WriteLine(report.ToString());

		// Assert — the corpus must be large enough to train the index, or every ANN row above is
		// a silent exact scan and the comparison is meaningless.
		await Assert.That(trained["corpus_raw"]).IsTrue();
		await Assert.That(trained["corpus_norm"]).IsTrue();

		double Mrr(string table, Func<string, string, string, string> sql) {
			var total = 0d;

			for (var i = 0; i < Targets.Length; i++) {
				var ranked = Ranked(connection, sql(table, Targets[i].Question, Vector(queryVectors[i].Vector.Span)));
				var rank   = ranked.IndexOf(Targets[i].Id);

				if (rank >= 0)
					total += 1d / (rank + 1);
			}

			return total / Targets.Length;
		}
	}

	[Test]
	public async ValueTask sweeps_ivf_partitions_against_nprobs(CancellationToken cancellationToken) {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = MemorySeeding.NewDataSources(dir.Path);
		using var connection  = dataSources.OpenLanceWriter();
		using var embeddings  = new Pmm12EmbeddingGenerator();

		var options = new EmbeddingGenerationOptions { Dimensions = KontextIndexConstants.VectorsDimension };
		var corpus  = BuildCorpus();

		var texts   = corpus.Select(record => JsonNormalizer.Instance.Normalize(Encoding.UTF8.GetBytes(record.Json))!).ToArray();
		var vectors = await embeddings.GenerateAsync(texts, options, cancellationToken);

		const string table = "ivf_sweep";

		Exec(connection, $"CREATE TABLE ldb.main.{table} (id BIGINT, text VARCHAR, embedding FLOAT[{Dimension}])");

		for (var i = 0; i < texts.Length; i++)
			Exec(connection, $"INSERT INTO ldb.main.{table} VALUES ({i}, '{texts[i].Replace("'", "''")}', {Vector(vectors[i].Vector.Span)})");

		var queryVectors = await embeddings.GenerateAsync(Targets.Select(target => target.Question), options, cancellationToken);

		int[] partitionCounts = [1, 8, 20, 64];
		int[] probeCounts     = [1, 4, 16, 64];

		var report = new StringBuilder();

		report.AppendLine();
		report.AppendLine($"normalized corpus {corpus.Length}   k {K}   vector MRR");
		report.AppendLine($"| num_partitions | {string.Join(" | ", probeCounts.Select(probes => $"nprobs {probes}"))} | refine 50 |");
		report.AppendLine($"|---|{string.Concat(probeCounts.Select(_ => "---|"))}---|");

		// Act
		foreach (var partitions in partitionCounts) {
			try {
				Exec(connection, $"DROP INDEX embedding_ivx ON ldb.main.{table}");
			} catch (Exception) {
				// The first pass has no index to drop.
			}

			Exec(
				connection,
				$"""
				 CREATE INDEX embedding_ivx ON ldb.main.{table} (embedding) USING IVF_HNSW_PQ
				 WITH (metric_type = 'l2', num_partitions = {partitions}, num_sub_vectors = {Dimension},
				       num_bits = 8, hnsw_m = 16, hnsw_ef_construction = 100)
				 """);

			var cells = probeCounts
				.Select(probes => Mrr($", nprobs := {probes}"))
				.Append(Mrr(", refine_factor := 50"))
				.Select(mrr => $"{mrr:F4}");

			report.AppendLine($"| {partitions} | {string.Join(" | ", cells)} |");
		}

		Console.WriteLine(report.ToString());

		await Assert.That(partitionCounts).IsNotEmpty();

		double Mrr(string knob) {
			var total = 0d;

			for (var i = 0; i < Targets.Length; i++) {
				var sql =
					$"SELECT id, _distance FROM lance_vector_search('ldb.main.{table}', 'embedding', " +
					$"CAST({Vector(queryVectors[i].Vector.Span)} AS FLOAT[{Dimension}]), k := {K}, prefilter := true{knob}) ORDER BY _distance";

				var rank = Ranked(connection, sql).IndexOf(Targets[i].Id);

				if (rank >= 0)
					total += 1d / (rank + 1);
			}

			return total / Targets.Length;
		}
	}

	[Test]
	public async ValueTask sweeps_vector_index_types(CancellationToken cancellationToken) {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = MemorySeeding.NewDataSources(dir.Path);
		using var connection  = dataSources.OpenLanceWriter();
		using var embeddings  = new Pmm12EmbeddingGenerator();

		var options = new EmbeddingGenerationOptions { Dimensions = KontextIndexConstants.VectorsDimension };
		var corpus  = BuildCorpus();

		var texts   = corpus.Select(record => JsonNormalizer.Instance.Normalize(Encoding.UTF8.GetBytes(record.Json))!).ToArray();
		var vectors = await embeddings.GenerateAsync(texts, options, cancellationToken);

		const string table = "type_sweep";

		Exec(connection, $"CREATE TABLE ldb.main.{table} (id BIGINT, text VARCHAR, embedding FLOAT[{Dimension}])");

		for (var i = 0; i < texts.Length; i++)
			Exec(connection, $"INSERT INTO ldb.main.{table} VALUES ({i}, '{texts[i].Replace("'", "''")}', {Vector(vectors[i].Vector.Span)})");

		var queryVectors = await embeddings.GenerateAsync(Targets.Select(target => target.Question), options, cancellationToken);

		// num_sub_vectors 48 is the documented dimension // 8; 384 is what VectorIndexOptions ships.
		(string Name, string Spec)[] builds = [
			("IVF_PQ p1 sub48",           "USING IVF_PQ WITH (metric_type = 'l2', num_partitions = 1, num_sub_vectors = 48, num_bits = 8)"),
			("IVF_PQ p20 sub48",          "USING IVF_PQ WITH (metric_type = 'l2', num_partitions = 20, num_sub_vectors = 48, num_bits = 8)"),
			("IVF_PQ p1 sub384",          "USING IVF_PQ WITH (metric_type = 'l2', num_partitions = 1, num_sub_vectors = 384, num_bits = 8)"),
			("IVF_HNSW_PQ p1 sub48",      "USING IVF_HNSW_PQ WITH (metric_type = 'l2', num_partitions = 1, num_sub_vectors = 48, num_bits = 8, hnsw_m = 16, hnsw_ef_construction = 100)"),
			("IVF_HNSW_PQ p1 sub384",     "USING IVF_HNSW_PQ WITH (metric_type = 'l2', num_partitions = 1, num_sub_vectors = 384, num_bits = 8, hnsw_m = 16, hnsw_ef_construction = 100)"),
			("IVF_HNSW_PQ p1 ef500",      "USING IVF_HNSW_PQ WITH (metric_type = 'l2', num_partitions = 1, num_sub_vectors = 48, num_bits = 8, hnsw_m = 16, hnsw_ef_construction = 500)"),
			("IVF_HNSW_SQ p1",            "USING IVF_HNSW_SQ WITH (metric_type = 'l2', num_partitions = 1, num_bits = 8, hnsw_m = 16, hnsw_ef_construction = 100)"),
			("IVF_HNSW_FLAT p1",          "USING IVF_HNSW_FLAT WITH (metric_type = 'l2', num_partitions = 1, hnsw_m = 16, hnsw_ef_construction = 100)"),
			("IVF_FLAT p1",               "USING IVF_FLAT WITH (metric_type = 'l2', num_partitions = 1)"),
		];

		var report = new StringBuilder();

		report.AppendLine();
		report.AppendLine($"normalized corpus {corpus.Length}   k {K}   vector MRR");
		report.AppendLine("| index build | plain | nprobs 16 | refine 10 | refine 50 |");
		report.AppendLine("|---|---|---|---|---|");

		// Act
		foreach (var (name, spec) in builds) {
			try {
				Exec(connection, $"DROP INDEX embedding_ivx ON ldb.main.{table}");
			} catch (Exception) {
				// Nothing to drop on the first pass.
			}

			try {
				Exec(connection, $"CREATE INDEX embedding_ivx ON ldb.main.{table} (embedding) {spec}");
			} catch (Exception ex) {
				report.AppendLine($"| {name} | REJECTED: {ex.Message.ReplaceLineEndings(" ")[..Math.Min(60, ex.Message.Length)]} | | | |");
				continue;
			}

			report.AppendLine($"| {name} | {Mrr(""):F4} | {Mrr(", nprobs := 16"):F4} | {Mrr(", refine_factor := 10"):F4} | {Mrr(", refine_factor := 50"):F4} |");
		}

		Console.WriteLine(report.ToString());

		await Assert.That(builds).IsNotEmpty();

		double Mrr(string knob) {
			var total = 0d;

			for (var i = 0; i < Targets.Length; i++) {
				var sql =
					$"SELECT id, _distance FROM lance_vector_search('ldb.main.{table}', 'embedding', " +
					$"CAST({Vector(queryVectors[i].Vector.Span)} AS FLOAT[{Dimension}]), k := {K}, prefilter := true{knob}) ORDER BY _distance";

				var rank = Ranked(connection, sql).IndexOf(Targets[i].Id);

				if (rank >= 0)
					total += 1d / (rank + 1);
			}

			return total / Targets.Length;
		}
	}

	static IEnumerable<(string Leg, Func<string, string, string, string> Sql)> Legs() {
		yield return ("fts", static (table, query, _) =>
			$"SELECT id, _score FROM lance_fts('ldb.main.{table}', 'text', '{query}', k := {K}, prefilter := true) ORDER BY _score DESC");

		foreach (var useIndex in new[] { false, true }) {
			var suffix = useIndex ? "index" : "exact";

			yield return ($"vector ({suffix})", (table, _, vector) =>
				$"SELECT id, _distance FROM lance_vector_search('ldb.main.{table}', 'embedding', CAST({vector} AS FLOAT[{Dimension}]), k := {K}, prefilter := true, use_index := {useIndex.ToString().ToLowerInvariant()}) ORDER BY _distance");

			foreach (var alpha in new[] { 0.2, 0.45, 0.7 })
				yield return ($"hybrid a{alpha:F2} ({suffix})", (table, query, vector) =>
					$"SELECT id, _hybrid_score FROM lance_hybrid_search('ldb.main.{table}', 'embedding', CAST({vector} AS FLOAT[{Dimension}]), 'text', '{query}', k := {K}, prefilter := true, alpha := {alpha.ToString(CultureInfo.InvariantCulture)}, use_index := {useIndex.ToString().ToLowerInvariant()}) ORDER BY _hybrid_score DESC");
		}

		foreach (var refine in new[] { 5, 10, 50 })
			yield return ($"vector (index) refine {refine}", (table, _, vector) =>
				$"SELECT id, _distance FROM lance_vector_search('ldb.main.{table}', 'embedding', CAST({vector} AS FLOAT[{Dimension}]), k := {K}, prefilter := true, refine_factor := {refine}) ORDER BY _distance");

		foreach (var nprobs in new[] { 4, 32 })
			yield return ($"vector (index) nprobs {nprobs}", (table, _, vector) =>
				$"SELECT id, _distance FROM lance_vector_search('ldb.main.{table}', 'embedding', CAST({vector} AS FLOAT[{Dimension}]), k := {K}, prefilter := true, nprobs := {nprobs}) ORDER BY _distance");

		yield return ("hybrid a0.45 oversample 16 (index)", static (table, query, vector) =>
			$"SELECT id, _hybrid_score FROM lance_hybrid_search('ldb.main.{table}', 'embedding', CAST({vector} AS FLOAT[{Dimension}]), 'text', '{query}', k := {K}, prefilter := true, alpha := 0.45, oversample_factor := 16) ORDER BY _hybrid_score DESC");

		yield return ("hybrid a0.45 refine 10 (index)", static (table, query, vector) =>
			$"SELECT id, _hybrid_score FROM lance_hybrid_search('ldb.main.{table}', 'embedding', CAST({vector} AS FLOAT[{Dimension}]), 'text', '{query}', k := {K}, prefilter := true, alpha := 0.45, refine_factor := 10) ORDER BY _hybrid_score DESC");
	}

	// Eight records carrying a tool and operator that appear exactly once in the corpus, each
	// paired with a question that names neither a JSON key nor an exact value from the payload.
	static readonly (long Id, string Question)[] Targets = [
		(CorpusSize + 0, "how long did the terraform plan take to finish"),
		(CorpusSize + 1, "which run was cancelled while packaging the helm chart"),
		(CorpusSize + 2, "who reindexed the customer search catalogue"),
		(CorpusSize + 3, "what happened when the certificate rotation ran"),
		(CorpusSize + 4, "which job uploaded the flame graph"),
		(CorpusSize + 5, "how big was the memory dump that was captured"),
		(CorpusSize + 6, "which run failed while migrating the billing schema"),
		(CorpusSize + 7, "what was the outcome of the load test against staging"),
	];

	static (long Id, string Json)[] BuildCorpus() {
		string[] tools      = ["bash", "grep", "pytest", "cargo", "msbuild", "docker", "npm", "dotnet"];
		string[] operators  = ["ripley", "dallas", "lambert", "kane", "parker", "brett"];
		string[] workspaces = ["kurrentdb", "aspire", "lance", "surge", "kontext"];

		var records = new List<(long, string)>(CorpusSize + Targets.Length);

		for (var i = 0; i < CorpusSize; i++)
			records.Add((i, Record(
				tools[i % tools.Length],
				$"{tools[i % tools.Length]} run step {i}",
				i % 3 == 0 ? 1 : 0,
				100 + i * 7,
				$"s-{i}",
				workspaces[i % workspaces.Length],
				operators[i % operators.Length],
				$"[\"ci\", \"{workspaces[i % workspaces.Length]}\"]",
				$"/tmp/step-{i}.log",
				1000 + i)));

		(string Tool, string Command, string Operator, string Artifact)[] targets = [
			("terraform", "terraform plan -out infra.tfplan",            "hicks",   "/tmp/infra-plan.txt"),
			("helm",      "helm package charts/gateway",                 "vasquez", "/tmp/gateway-chart.tgz"),
			("reindexer", "reindex customer search catalogue",           "hudson",  "/tmp/catalogue-reindex.log"),
			("certbot",   "rotate the wildcard tls certificate",         "gorman",  "/tmp/rotation-audit.log"),
			("profiler",  "collect a flame graph of the write path",     "drake",   "/tmp/flame-graph.svg"),
			("dumper",    "capture a full process memory dump",          "apone",   "/tmp/process-dump.bin"),
			("flyway",    "migrate the billing schema to revision 42",   "ferro",   "/tmp/billing-migration.log"),
			("k6",        "run a load test against the staging cluster", "spunkmeyer", "/tmp/load-test-summary.json"),
		];

		for (var i = 0; i < targets.Length; i++) {
			var (tool, command, op, artifact) = targets[i];

			records.Add((CorpusSize + i, Record(
				tool, command, i % 2, 4200 + i * 311, $"t-{i}", "kurrentdb", op,
				$"[\"{tool}\", \"release\"]", artifact, 50_000 + i)));
		}

		return records.ToArray();
	}

	static string Record(
		string tool, string command, int exitCode, int durationMs,
		string sessionId, string workspace, string op, string tags, string path, int bytes
	) =>
		$$"""
		  {"toolName":"{{tool}}","commandLine":"{{command}}","exitCode":{{exitCode}},"durationMs":{{durationMs}},"session":{"sessionId":"{{sessionId}}","workspace":"{{workspace}}","operator":"{{op}}"},"tags":{{tags}},"artifacts":[{"path":"{{path}}","bytes":{{bytes}}}],"succeeded":{{(exitCode == 0 ? "true" : "false")}}}
		  """;

	const string CodeOptions =
		"""
		    replace           = true,
		    analyzer          = 'code',
		    base_tokenizer    = 'code',
		    split_identifiers = true,
		    split_on_numerics = true,
		    preserve_original = true,
		    stem              = false,
		    remove_stop_words = false,
		    max_token_length  = 1048576
		""";

	static List<long> Ranked(DuckDBAdvancedConnection connection, string sql) {
		var ids = new List<long>();

		using var result = connection.ExecuteAdHocQuery(sql);

		while (result.TryFetch(out var chunk)) {
			while (chunk.TryRead(out var row))
				ids.Add(row.ReadInt64());

			chunk.Dispose();
		}

		return ids;
	}

	static string Vector(ReadOnlySpan<float> vector) {
		var builder = new StringBuilder("[");

		for (var i = 0; i < vector.Length; i++) {
			if (i > 0)
				builder.Append(',');

			builder.Append(vector[i].ToString("R", CultureInfo.InvariantCulture));
		}

		return builder.Append(']').ToString();
	}

	static void Exec(DuckDBAdvancedConnection connection, string sql) {
		using var command = connection.CreateCommand();
		command.CommandText = sql;
		command.ExecuteNonQuery();
	}
}
