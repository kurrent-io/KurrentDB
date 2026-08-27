// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Globalization;
using System.Text;
using Kurrent.Kontext.Data;
using Kurrent.Kontext.Embeddings.SentencePieceOnnx;
using Kurrent.Kontext.Infrastructure.Data;
using Kurrent.Kontext.Infrastructure.Data.LanceDB;
using Kurrent.Kontext.Testing;
using Kurrent.Quack;
using Microsoft.Extensions.AI;

namespace Kurrent.Kontext.Tests.Integration.Data;

/// <summary>
/// Whether this stack can hold a record's chunks as ONE row with a multivector column, instead of a
/// chunk table joined back to records.
/// </summary>
/// <remarks>
/// <para>Lance scores a <c>list&lt;list&lt;float32, dim&gt;&gt;</c> column with MaxSim — the maximum
/// similarity per query vector against every vector in the row, summed. That is the collapse a chunk
/// table would otherwise do with GROUP BY, done inside the engine, so one row stays one record and
/// the pushdown filters keep working on the columns they already work on.</para>
/// <para>Three things have to hold and none of them are documented for our fork: DuckDB has to
/// round-trip the nested array, the index builder has to accept the column, and hybrid search has to
/// fuse an FTS leg that is per-row against a vector leg that is per-chunk. The probe answers each
/// separately so a failure names which one.</para>
/// </remarks>
[Category("Integration")]
[Timeout(300_000)]
public class MultivectorChunkingProbeTests {
    const int Dim = KontextIndexConstants.VectorsDimension;

    [Test]
    public async ValueTask reports_whether_a_multivector_column_survives_write_index_and_search(
        CancellationToken cancellationToken) {
        // Arrange
        using var dir         = new TempDir();
        using var dataSources = MemorySeeding.NewDataSources(dir.Path);
        using var connection  = dataSources.OpenLanceWriter();
        using var embeddings  = new Pmm12EmbeddingGenerator();

        var options = new EmbeddingGenerationOptions { Dimensions = Dim };
        var report  = new StringBuilder().AppendLine();

        // One long document, split into chunks the way a records indexer would: the normalizer emits
        // one "key: value" line per pair, so lines are the natural boundary and no value is cut.
        var documents = Documents();
        var chunked   = documents.Select(d => d.Split('\n', StringSplitOptions.RemoveEmptyEntries)).ToArray();

        // Act — 1. can DuckDB hold FLOAT[dim][] at all?
        var wrote = Probe(report, "write   FLOAT[{dim}][] column", () => {
            Exec(connection, $"CREATE TABLE ldb.main.mv (id BIGINT, text VARCHAR, embedding FLOAT[{Dim}][])");
            return true;
        });

        if (wrote)
            for (var i = 0; i < chunked.Length; i++) {
                var vectors = await embeddings.GenerateAsync(chunked[i], options, cancellationToken);
                var literal = string.Join(", ", vectors.Select(v => Vector(v.Vector.Span)));

                Exec(connection,
                    $"INSERT INTO ldb.main.mv VALUES ({i}, '{documents[i].Replace("'", "''")}', [{literal}])");
            }

        // 2. does the index builder accept it? The docs say multivector is cosine-only and IVF_PQ,
        // while the memories index is built l2 — so this is where a mismatch would surface.
        // Cosine, not the L2 default: Lance rejects any other metric on a multivector column. Safe
        // for us either way — the generators L2-normalize, and on unit vectors cosine and l2 rank
        // identically.
        var indexed = wrote && Probe(report, "build vector index (cosine)", () =>
            dataSources.Execute(c => c.EnsureVectorIndex("ldb.main.mv", "embedding",
                new LanceIvfPqIndexOptions {
                    MetricType    = LanceMetricType.Cosine,
                    NumPartitions = 1,
                    NumSubVectors = Dim / 8,
                })));

        // 3. does a single query vector score against every chunk, and does it return ONE row per
        // document rather than one per chunk?
        var query = (await embeddings.GenerateAsync(["the payment was declined because the card expired"],
            options, cancellationToken))[0];

        // Not gated on the index: an untrained table still answers by brute force, and the question
        // here is what search RETURNS, not how fast it got there.
        List<long> rows = [];
        if (wrote)
            Probe(report, "vector search returns rows", () => {
                rows = Query(connection,
                    $"SELECT id FROM lance_vector_search('ldb.main.mv', 'embedding', "
                  + $"CAST({Vector(query.Vector.Span)} AS FLOAT[{Dim}]), k := 10)");
                return rows.Count > 0;
            });

        report.AppendLine($"rows returned : {rows.Count}   distinct ids : {rows.Distinct().Count()}   documents : {documents.Length}");
        report.AppendLine(rows.Count switch {
            0                                     => "no rows — nothing to conclude about collapsing.",
            _ when rows.Count == rows.Distinct().Count() => "one row per document — Lance collapsed the chunks itself.",
            _                                     => "DUPLICATE ids — the chunks are surfacing as separate rows.",
        });

        // The distinctive line sits at line 37 of document 0, past pmm12's 128-token window. It is
        // only reachable if the chunk vectors are being scored, so this is the result that matters.
        report.AppendLine(rows.Count > 0
            ? $"top hit : document {rows[0]}   (0 = the chunk beyond the window was matched)"
            : "top hit : none");

        // 4. the one that decides the design: can hybrid fuse per-row FTS with per-chunk vectors?
        if (wrote) {
            Exec(connection, "CREATE INDEX mv_fts ON ldb.main.mv (text) USING INVERTED WITH (replace = true)");
            dataSources.Execute(c => c.EnsureInvertedIndex("ldb.main.mv", "text"));

            Probe(report, "hybrid search over multivector", () => {
                var hits = Query(connection,
                    $"SELECT id FROM lance_hybrid_search('ldb.main.mv', 'embedding', "
                  + $"CAST({Vector(query.Vector.Span)} AS FLOAT[{Dim}]), 'text', 'expired card', "
                  + "k := 10, alpha := 0.5, prefilter := true, refine_factor := 4, oversample_factor := 4)");

                report.AppendLine($"hybrid rows   : {hits.Count}   distinct : {hits.Distinct().Count()}"
                                + (hits.Count > 0 ? $"   top : document {hits[0]}" : ""));
                return hits.Count > 0;
            });
        }

        Console.WriteLine(report.ToString());

        // Assert — the probe records what the engine does; it does not assert a preferred answer.
        await Assert.That(report.Length).IsGreaterThan(0);
    }

    // Enough rows to clear the PQ training floor — the engine refuses under 256 — each long enough
    // that its tail sits outside pmm12's 128-token window. The one distinctive line is buried at
    // position 37 of document 0, so retrieving that document proves the later chunks were scored.
    const int DocumentCount = 300;   // the PQ trainer counts ROWS, and refuses under 256
    const int ChunksPerDoc  = 12;    // ~12 tokens a line, so the tail clears the 128-token window

    static string[] Documents() => [.. Enumerable.Range(0, DocumentCount).Select(doc =>
        string.Join('\n', Enumerable.Range(0, ChunksPerDoc).Select(line =>
            line == ChunksPerDoc - 1 && doc == 0
                ? "failure reason: the payment was declined because the card expired,"
                : $"field {doc} {line}: some routine value that carries no particular meaning,")))];

    static List<long> Query(DuckDBAdvancedConnection connection, string sql) {
        using var command = connection.CreateCommand();
        command.CommandText = sql;

        using var reader = command.ExecuteReader();

        List<long> ids = [];
        while (reader.Read())
            ids.Add(reader.GetInt64(0));

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

    static bool Probe(StringBuilder report, string what, Func<bool> act) {
        try {
            var ok = act();
            report.AppendLine($"{what,-34} : {(ok ? "OK" : "returned false")}");
            return ok;
        } catch (Exception ex) {
            report.AppendLine($"{what,-34} : FAILED — {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
            return false;
        }
    }
}
