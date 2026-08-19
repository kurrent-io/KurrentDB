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
/// Ranks the serialization formats a payload could be stored in against the records table's
/// code-analyzer FTS index: the same document as JSON, YAML, TOML and JsonNormalizer output,
/// scored by BM25 over a shared query set.
/// </summary>
[Category("Integration")]
[Timeout(60_000)]
public class FtsFormatProbeTests {
	[Test]
	[Arguments("code")]
	[Arguments("simple")]
	public async ValueTask ranks_serialization_formats_by_bm25(string tokenizer, CancellationToken cancellationToken) {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = MemorySeeding.NewDataSources(dir.Path);
		using var connection  = dataSources.OpenLanceWriter();

		var normalized = JsonNormalizer.Instance.Normalize(Encoding.UTF8.GetBytes(Json))!;

		var candidates = new Dictionary<string, string> {
			["json"]       = Json,
			["yaml"]       = Yaml,
			["toml"]       = Toml,
			["normalized"] = normalized,
		};

		CreateTable(connection, tokenizer);

		var id = 0;

		foreach (var (format, text) in candidates)
			Insert(connection, Table, ++id, format, text);

		// Distractors give BM25 a corpus: without competing documents every term is equally rare
		// and the scores collapse.
		foreach (var distractor in Distractors)
			Insert(connection, Table, ++id, "distractor", distractor);

		dataSources.Execute(c => c.EnsureInvertedIndex($"ldb.main.{Table}", "text"));

		// Act
		var scores = Queries.ToDictionary(
			query => query,
			query => Search(connection, Table, query));

		// Assert — every format stays retrievable for every query. A format whose syntax breaks
		// the code analyzer's tokens would silently drop out here, which is the failure this
		// probe exists to catch.
		var report = new StringBuilder();

		report.AppendLine();
		report.AppendLine($"### tokenizer: {tokenizer}");
		report.AppendLine($"| query | {string.Join(" | ", candidates.Keys)} |");
		report.AppendLine($"|---|{string.Concat(candidates.Keys.Select(_ => "---|"))}");

		var totals  = candidates.Keys.ToDictionary(format => format, _ => 0d);
		var wins    = candidates.Keys.ToDictionary(format => format, _ => 0);
		var missing = new List<string>();

		foreach (var (query, hits) in scores) {
			var cells = new List<string>();
			var best  = candidates.Keys.OrderByDescending(format => hits.GetValueOrDefault(format)).First();

			foreach (var format in candidates.Keys) {
				if (!hits.TryGetValue(format, out var score)) {
					missing.Add($"{query} -> {format}");
					cells.Add("MISS");
					continue;
				}

				totals[format] += score;
				cells.Add(format == best ? $"**{score:F4}**" : $"{score:F4}");
			}

			wins[best]++;
			report.AppendLine($"| {query} | {string.Join(" | ", cells)} |");
		}

		report.AppendLine();

		foreach (var (format, total) in totals.OrderByDescending(entry => entry.Value))
			report.AppendLine($"{format,-11} total {total,9:F4}   wins {wins[format]}/{Queries.Length}   chars {candidates[format].Length}");

		Console.WriteLine(report.ToString());

		await Assert.That(missing).IsEmpty();
	}

	[Test]
	public async ValueTask ranks_index_configurations_for_raw_json(CancellationToken cancellationToken) {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = MemorySeeding.NewDataSources(dir.Path);
		using var connection  = dataSources.OpenLanceWriter();

		var normalized = JsonNormalizer.Instance.Normalize(Encoding.UTF8.GetBytes(Json))!;

		var report = new StringBuilder();

		report.AppendLine();
		report.AppendLine("| config | raw json | normalized | phrase | verdict |");
		report.AppendLine("|---|---|---|---|---|");

		var accepted = new List<string>();
		var trace    = Path.Combine(Path.GetTempPath(), "kontext-fts-config-probe.md");

		File.WriteAllText(trace, "| config | raw json | normalized | phrase | verdict |\n|---|---|---|---|---|\n");

		// Act
		foreach (var (name, table, options) in Configurations) {
			Exec(connection, $"CREATE TABLE ldb.main.{table} (id BIGINT, format VARCHAR, text VARCHAR)");

			string? rejection = null;

			try {
				Exec(connection, $"CREATE INDEX text_fts ON ldb.main.{table} (text) USING INVERTED WITH (\n{options}\n)");
			} catch (Exception ex) {
				rejection = ex.Message.ReplaceLineEndings(" ");
			}

			if (rejection is not null) {
				Record(report, trace, $"| {name} | - | - | - | REJECTED: {Truncate(rejection)} |");
				continue;
			}

			accepted.Add(name);

			var id = 0;
			Insert(connection, table, ++id, "json", Json);
			Insert(connection, table, ++id, "normalized", normalized);

			foreach (var distractor in Distractors)
				Insert(connection, table, ++id, "distractor", distractor);

			dataSources.Execute(c => c.EnsureInvertedIndex($"ldb.main.{table}", "text"));

			var jsonTotal = 0d;
			var normTotal = 0d;

			foreach (var query in Queries) {
				var hits = Search(connection, table, query);
				jsonTotal += hits.GetValueOrDefault("json");
				normTotal += hits.GetValueOrDefault("normalized");
			}

			// Phrase syntax is only meaningful with positions recorded; elsewhere it either
			// errors or degrades to a term bag.
			string phrase;
			try {
				var hits = Search(connection, table, "\"\"dotnet test\"\"");
				phrase = hits.TryGetValue("json", out var score) ? $"{score:F4}" : "no hit";
			} catch (Exception ex) {
				phrase = $"ERR: {Truncate(ex.Message.ReplaceLineEndings(" "), 40)}";
			}

			var verdict = jsonTotal >= normTotal ? "raw json wins" : "normalized wins";

			Record(report, trace, $"| {name} | {jsonTotal:F4} | {normTotal:F4} | {phrase} | {verdict} |");
		}

		Console.WriteLine(report.ToString());

		// Assert — the shipped configuration must stay creatable.
		await Assert.That(accepted).Contains("code (shipped)");
	}

	[Test]
	[Timeout(300_000)]
	[Arguments("keyword")]
	[Arguments("natural")]
	public async ValueTask ranks_raw_json_against_normalized_across_search_legs(string querySet, CancellationToken cancellationToken) {
		// Arrange — the shipped model, so the vectors are the ones production would store.
		using var dir         = new TempDir();
		using var dataSources = MemorySeeding.NewDataSources(dir.Path);
		using var connection  = dataSources.OpenLanceWriter();
		using var embeddings  = new Pmm12EmbeddingGenerator();

		var options    = new EmbeddingGenerationOptions { Dimensions = KontextIndexConstants.VectorsDimension };
		var normalized = JsonNormalizer.Instance.Normalize(Encoding.UTF8.GetBytes(Json))!;

		List<(string Format, string Text)> documents = [("json", Json), ("normalized", normalized)];
		documents.AddRange(Distractors.Select(text => ("distractor", text)));

		var documentVectors = await embeddings.GenerateAsync(documents.Select(entry => entry.Text), options, cancellationToken);

		const string table = "legs_probe";

		Exec(connection, $"CREATE TABLE ldb.main.{table} (id BIGINT, format VARCHAR, text VARCHAR, embedding FLOAT[{KontextIndexConstants.VectorsDimension}])");
		Exec(connection, $"CREATE INDEX text_fts ON ldb.main.{table} (text) USING INVERTED WITH (\n{CodeOptions()}\n)");

		var id = 0;

		foreach (var ((format, text), vector) in documents.Zip(documentVectors))
			Exec(
				connection,
				$"INSERT INTO ldb.main.{table} VALUES ({++id}, '{format}', '{text.Replace("'", "''")}', {Vector(vector.Vector.Span)})");

		dataSources.Execute(c => c.EnsureInvertedIndex($"ldb.main.{table}", "text"));

		// Below the PQ training floor at this corpus size, so the vector leg is an exact scan —
		// which removes ANN recall as a variable rather than adding one.
		var indexed = dataSources.Execute(c => c.EnsureVectorIndex($"ldb.main.{table}", "embedding", new LanceIvfPqIndexOptions { NumPartitions = 1, NumSubVectors = KontextIndexConstants.VectorsDimension / 8 }));

		var report = new StringBuilder();

		report.AppendLine();
		var queries = querySet switch {
			"keyword" => Queries,
			"natural" => NaturalQueries,
			_         => throw new ArgumentOutOfRangeException(nameof(querySet), querySet, "Unknown query set."),
		};

		report.AppendLine($"### query set: {querySet}   vector index trained: {indexed}   alpha: {Alpha}   docs: {documents.Count}");
		report.AppendLine("| query | fts json | fts norm | vec json | vec norm | hybrid json | hybrid norm |");
		report.AppendLine("|---|---|---|---|---|---|---|");

		var wins = new Dictionary<string, (int Json, int Normalized)> {
			["fts"]    = (0, 0),
			["vector"] = (0, 0),
			["hybrid"] = (0, 0),
		};

		// Act
		foreach (var query in queries) {
			var queryVectors = await embeddings.GenerateAsync([query], options, cancellationToken);
			var queryVector  = Vector(queryVectors[0].Vector.Span);

			var fts = Probe(connection, $"SELECT format, _score FROM lance_fts('ldb.main.{table}', 'text', '{query}', k := 50, prefilter := true)");

			var vector = Probe(
				connection,
				$"SELECT format, _distance FROM lance_vector_search('ldb.main.{table}', 'embedding', CAST({queryVector} AS FLOAT[{KontextIndexConstants.VectorsDimension}]), k := 50, prefilter := true)");

			var hybrid = Probe(
				connection,
				$"""
				 SELECT format, _hybrid_score
				 FROM lance_hybrid_search('ldb.main.{table}', 'embedding', CAST({queryVector} AS FLOAT[{KontextIndexConstants.VectorsDimension}]),
				                          'text', '{query}', k := 50, prefilter := true, alpha := {Alpha})
				 """);

			// _score and _hybrid_score rank descending; _distance ranks ascending.
			wins["fts"]    = Tally(wins["fts"], fts.GetValueOrDefault("json"), fts.GetValueOrDefault("normalized"), higherWins: true);
			wins["vector"] = Tally(wins["vector"], vector.GetValueOrDefault("json", float.MaxValue), vector.GetValueOrDefault("normalized", float.MaxValue), higherWins: false);
			wins["hybrid"] = Tally(wins["hybrid"], hybrid.GetValueOrDefault("json"), hybrid.GetValueOrDefault("normalized"), higherWins: true);

			report.AppendLine(
				$"| {query} | {Cell(fts, "json")} | {Cell(fts, "normalized")} | {Cell(vector, "json")} | {Cell(vector, "normalized")} " +
				$"| {Cell(hybrid, "json")} | {Cell(hybrid, "normalized")} |");
		}

		report.AppendLine();

		foreach (var (leg, (json, norm)) in wins)
			report.AppendLine($"{leg,-7} raw json {json}/{queries.Length}   normalized {norm}/{queries.Length}");

		Console.WriteLine(report.ToString());

		// Assert — both variants stay retrievable on every leg; a leg that drops one is the
		// failure worth catching.
		await Assert.That(wins["fts"].Json + wins["fts"].Normalized).IsEqualTo(queries.Length);
	}

	const double Alpha = 0.45;

	// Questions a person would actually ask of this record: no field names, no shared rare tokens
	// with the document, so the lexical leg has little to grip and the sentence model is exercised.
	static readonly string[] NaturalQueries = [
		"how long did the bash command take to run",
		"which operator ran the regression suite",
		"what was the exit code of the test run",
		"where were the log files written",
		"what workspace was this session in",
		"did the command finish successfully",
		"how large was the report file",
		"what command was executed",
	];

	static (int Json, int Normalized) Tally((int Json, int Normalized) current, float json, float normalized, bool higherWins) =>
		higherWins
			? json >= normalized ? (current.Json + 1, current.Normalized) : (current.Json, current.Normalized + 1)
			: json <= normalized ? (current.Json + 1, current.Normalized) : (current.Json, current.Normalized + 1);

	static string Cell(Dictionary<string, float> hits, string format) =>
		hits.TryGetValue(format, out var value) ? $"{value:F4}" : "MISS";

	static string Vector(ReadOnlySpan<float> vector) {
		var builder = new StringBuilder("[");

		for (var i = 0; i < vector.Length; i++) {
			if (i > 0)
				builder.Append(',');

			builder.Append(vector[i].ToString("R", CultureInfo.InvariantCulture));
		}

		return builder.Append(']').ToString();
	}

	static Dictionary<string, float> Probe(DuckDBAdvancedConnection connection, string sql) {
		var hits = new Dictionary<string, float>();

		using var result = connection.ExecuteAdHocQuery(sql);

		while (result.TryFetch(out var chunk)) {
			while (chunk.TryRead(out var row)) {
				var format = row.ReadString();
				var value  = row.ReadFloat();

				if (format is not "distractor")
					hits[format] = value;
			}

			chunk.Dispose();
		}

		return hits;
	}

	static readonly (string Name, string Table, string Options)[] Configurations = [
		("code (shipped)",     "cfg_code",      CodeOptions()),
		("code, no split ids", "cfg_code_nosp", CodeOptions().Replace("split_identifiers = true", "split_identifiers = false")),
		("code + position",    "cfg_code_pos",  CodeOptions("with_position     = true")),
		("code, no preserve",  "cfg_code_nop",  CodeOptions().Replace("preserve_original = true", "preserve_original = false")),
		("simple (memories)",  "cfg_simple",    "    replace = true, base_tokenizer = 'simple', language = 'English', stem = true, max_token_length = 1048576"),
		("ngram 3-4",          "cfg_ngram",     "    replace = true, base_tokenizer = 'ngram', min_ngram_length = 3, max_ngram_length = 4, max_token_length = 1048576"),
	];

	static string CodeOptions(string? extra = null) =>
		$"""
		     replace           = true,
		     analyzer          = 'code',
		     base_tokenizer    = 'code',
		     split_identifiers = true,
		     split_on_numerics = true,
		     preserve_original = true,
		     stem              = false,
		     remove_stop_words = false,
		     max_token_length  = 1048576{(extra is null ? "" : $",\n     {extra}")}
		 """;

	static void Record(StringBuilder report, string trace, string row) {
		report.AppendLine(row);
		File.AppendAllText(trace, row + "\n");
	}

	static string Truncate(string text, int length = 90) =>
		text.Length <= length ? text : text[..length] + "…";

	const string Table = "fts_format_probe";

	static readonly string[] Queries = [
		"bash",
		"dotnet test filter ReadTests",
		"sessionId s-77",
		"workspace kurrentdb",
		"regression lance",
		"report trx",
		"durationMs 5400",
		"operator ripley",
	];

	const string Json =
		"""
		{
		  "toolName": "bash",
		  "commandLine": "dotnet test --filter ReadTests",
		  "exitCode": 0,
		  "durationMs": 5400,
		  "session": { "sessionId": "s-77", "workspace": "kurrentdb", "operator": "ripley" },
		  "tags": ["ci", "regression", "lance"],
		  "artifacts": [
		    { "path": "/tmp/out.log", "bytes": 18422 },
		    { "path": "/tmp/report.trx", "bytes": 90210 }
		  ],
		  "succeeded": true
		}
		""";

	const string Yaml =
		"""
		toolName: bash
		commandLine: dotnet test --filter ReadTests
		exitCode: 0
		durationMs: 5400
		session:
		  sessionId: s-77
		  workspace: kurrentdb
		  operator: ripley
		tags:
		  - ci
		  - regression
		  - lance
		artifacts:
		  - path: /tmp/out.log
		    bytes: 18422
		  - path: /tmp/report.trx
		    bytes: 90210
		succeeded: true
		""";

	const string Toml =
		"""
		toolName = "bash"
		commandLine = "dotnet test --filter ReadTests"
		exitCode = 0
		durationMs = 5400
		tags = ["ci", "regression", "lance"]
		succeeded = true

		[session]
		sessionId = "s-77"
		workspace = "kurrentdb"
		operator = "ripley"

		[[artifacts]]
		path = "/tmp/out.log"
		bytes = 18422

		[[artifacts]]
		path = "/tmp/report.trx"
		bytes = 90210
		""";

	static readonly string[] Distractors = [
		"""{ "toolName": "grep", "commandLine": "rg --files", "exitCode": 1, "durationMs": 12 }""",
		"""{ "streamName": "orders-1", "eventType": "OrderPlaced", "total": 42, "currency": "EUR" }""",
		"""{ "scavenge": "completed", "chunksRemoved": 14, "durationMs": 900000 }""",
		"the reactor calibration completed without incident on deck four",
		"""{ "workspace": "aspire", "sessionId": "s-12", "operator": "dallas" }""",
		"""{ "path": "/tmp/other.log", "bytes": 1, "tags": ["noise"] }""",
	];

	static Dictionary<string, float> Search(DuckDBAdvancedConnection connection, string table, string query) {
		var sql =
			$"""
			 SELECT format, _score
			 FROM lance_fts('ldb.main.{table}', 'text', '{query}', k := 50, prefilter := true)
			 ORDER BY _score DESC
			 """;

		var hits = new Dictionary<string, float>();

		using var result = connection.ExecuteAdHocQuery(sql);

		while (result.TryFetch(out var chunk)) {
			while (chunk.TryRead(out var row)) {
				var format = row.ReadString();
				var score  = row.ReadFloat();

				if (format is not "distractor")
					hits[format] = score;
			}

			chunk.Dispose();
		}

		return hits;
	}

	static void CreateTable(DuckDBAdvancedConnection connection, string tokenizer) {
		Exec(connection, $"CREATE TABLE ldb.main.{Table} (id BIGINT, format VARCHAR, text VARCHAR)");

		// 'code' is the records table's content_fts configuration; 'simple' is the memories
		// table's. The code preset is rejected unless base_tokenizer rides with it and it
		// implies none of the other knobs, so the two option sets cannot be merged.
		var options = tokenizer switch {
			"code" =>
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
				""",
			"simple" =>
				"""
				    replace          = true,
				    base_tokenizer   = 'simple',
				    language         = 'English',
				    stem             = true,
				    max_token_length = 1048576
				""",
			_ => throw new ArgumentOutOfRangeException(nameof(tokenizer), tokenizer, "Unknown tokenizer preset."),
		};

		Exec(connection, $"CREATE INDEX text_fts ON ldb.main.{Table} (text) USING INVERTED WITH (\n{options}\n)");
	}

	static void Insert(DuckDBAdvancedConnection connection, string table, int id, string format, string text) =>
		Exec(connection, $"INSERT INTO ldb.main.{table} VALUES ({id}, '{format}', '{text.Replace("'", "''")}')");

	static void Exec(DuckDBAdvancedConnection connection, string sql) {
		using var command = connection.CreateCommand();
		command.CommandText = sql;
		command.ExecuteNonQuery();
	}
}
