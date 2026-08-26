// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Kurrent.Kontext.Data;
using Kurrent.Quack;

namespace Kurrent.Kontext.Records.Data;

public sealed class KontextRecordsStore(KontextDataSource connections) {
    public async IAsyncEnumerable<RecordHit> SearchAsync(
        SearchOptions options,
        [EnumeratorCancellation] CancellationToken ct = default
    ) {
        if (options.Predicates.Count == 0)
            throw new ArgumentException("At least one predicate is required.", nameof(options));

        var args = new SearchRecordsArgs(JsonPredicate.Render(options.Predicates), options.K);

        var hits = await connections
            .ExecuteAsync(conn => conn.ExecuteQuery<SearchRecordsArgs, RecordHit, SearchRecordsQuery>(in args).ToAsyncEnumerable(), ct)
            .ConfigureAwait(false);

        await foreach (var hit in hits)
            yield return hit;
    }

    public async IAsyncEnumerable<RecordHit> SearchAsync(
        HybridOptions options,
        [EnumeratorCancellation] CancellationToken ct = default
    ) {
        var predicate = HybridSearchPredicate.Build(options);
        var args      = new HybridSearchArgs(options);

        var hits = await connections
            .ExecuteAsync(connection => {
                var query = new HybridSearchQuery(predicate);
                return connection
                    .ExecuteQuery<HybridSearchArgs, RecordHit, HybridSearchQuery>(ref query, in args)
                    .ToList();
            }, ct)
            .ConfigureAwait(false);

        foreach (var hit in hits)
            yield return hit;
    }

    public async IAsyncEnumerable<StoredRecord> ListAsync(
        ListOptions options,
        [EnumeratorCancellation] CancellationToken ct = default
    ) {
        var predicate = ListRecordsPredicate.Build(options);
        var args      = new ListRecordsArgs(options);

        var records = await connections
            .ExecuteAsync(conn => {
                var query = new ListRecordsQuery(predicate);
                return conn
                    .ExecuteQuery<ListRecordsArgs, StoredRecord, ListRecordsQuery>(ref query, in args)
                    .ToList();
            }, ct)
            .ConfigureAwait(false);

        foreach (var record in records)
            yield return record;
    }

    public ValueTask<StoredRecord?> GetAsync(Guid recordId, CancellationToken ct = default) {
        var args = new GetRecordArgs(recordId);

        return connections.ExecuteAsync<StoredRecord?>(connection => {
            var found = connection.QueryFirstOrDefault<GetRecordArgs, StoredRecord, GetRecordQuery>(in args);
            return found.HasValue ? found.Value : null;
        }, ct);
    }

    public ValueTask<StoredRecord?> GetAsync(long logPosition, CancellationToken ct = default) {
        var args = new GetRecordAtArgs(logPosition);

        return connections.ExecuteAsync<StoredRecord?>(connection => {
            var found = connection.QueryFirstOrDefault<GetRecordAtArgs, StoredRecord, GetRecordAtQuery>(in args);
            return found.HasValue ? found.Value : null;
        }, ct);
    }
}

// The predicate text and the bind order are one contract: every clause added here contributes
// its placeholders in this exact sequence, and ListRecordsQuery.Bind walks them in the same one.
static class ListRecordsPredicate {
    public static string Build(ListOptions options) {
        var predicate = new StringBuilder("log_position > ?");

        // IN lists push down into the engine; array_contains over a bound array does not.
        if (options.RecordIds is { Length: > 0 } ids)
            predicate.Append("\n  AND record_id IN (").Append(Placeholders(ids.Length)).Append(')');

        if (options.LogPositions is { Length: > 0 } positions)
            predicate.Append("\n  AND log_position IN (").Append(Placeholders(positions.Length)).Append(')');

        if (options.Stream is not null)
            predicate.Append("\n  AND stream = ?");

        if (options.Category is not null)
            predicate.Append("\n  AND category = ?");

        if (options.SchemaName is not null)
            predicate.Append("\n  AND schema_name = ?");

        if (options.SchemaFormat is not null)
            predicate.Append("\n  AND schema_format = ?");

        return predicate.ToString();
    }

    static string Placeholders(int count) => string.Join(", ", Enumerable.Repeat("?", count));
}

readonly record struct ListRecordsArgs(ListOptions Options);

file readonly record struct ListRecordsQuery(string Predicate) : IDynamicQuery<ListRecordsArgs, StoredRecord> {
    public static CompositeFormat CommandTemplate { get; } = CompositeFormat.Parse(
        """
        SELECT log_position, record_id, stream, category, schema_name, schema_id, schema_format, data, created_at, properties
        FROM ldb.main.records
        WHERE {0}
        ORDER BY log_position
        LIMIT ?
        """);

    public void FormatCommandTemplate(Span<object?> args) => args[0] = Predicate;

    public static StatementBindingResult Bind(in ListRecordsArgs args, PreparedStatement statement) {
        var options = args.Options;
        var index   = 1;

        statement.Bind(index++, options.AfterLogPosition);

        if (options.RecordIds is { Length: > 0 } ids) {
            foreach (var id in ids) {
                var recordId = id;
                statement.Bind(index++, MemoryMarshal.AsBytes(new ReadOnlySpan<Guid>(in recordId)), BlobType.Raw);
            }
        }

        if (options.LogPositions is { Length: > 0 } positions) {
            foreach (var position in positions)
                statement.Bind(index++, position);
        }

        if (options.Stream is { } stream)
            statement.Bind(index++, stream);

        if (options.Category is { } category)
            statement.Bind(index++, category);

        if (options.SchemaName is { } schemaName)
            statement.Bind(index++, schemaName);

        if (options.SchemaFormat is { } schemaFormat)
            statement.Bind(index++, schemaFormat);

        statement.Bind(index, options.Limit);

        return new(statement, completed: true);
    }

    public static StoredRecord Parse(ref DataChunk.Row row) => RecordReader.Read(ref row);
}

// The predicate text and the bind order are one contract: every clause added here contributes its
// placeholder in this exact sequence, and HybridSearchQuery.Bind walks them in the same one, after
// the four the table function itself takes.
static class HybridSearchPredicate {
    public static string Build(HybridOptions options) {
        var clauses = new List<string>(2);

        // The scope and schema dimensions are each a oneof in the contract, so the store never sees two
        // of a group set and needs no rule of its own.
        if (options.Stream is not null) clauses.Add("stream = ?");
        if (options.Category is not null) clauses.Add("category = ?");
        if (options.SchemaName is not null) clauses.Add("schema_name = ?");
        if (options.SchemaId is not null) clauses.Add("schema_id = ?");
        if (options.SchemaFormat is not null) clauses.Add("schema_format = ?");

        return clauses.Count == 0 ? "" : $"WHERE {string.Join("\n  AND ", clauses)}";
    }
}

readonly record struct HybridSearchArgs(HybridOptions Options);

// The WHERE looks like a post-filter and is not one. lance_hybrid_search takes no `filter :=` argument
// (lance_vector_search and lance_fts do), but the table function opts into DuckDB filter pushdown, so
// the predicate is evaluated inside candidate selection: a scoped page comes back FULL, drawn from the
// matching rows, never trimmed out of a top-k the filter never saw.
// Measured in RecordsFilterPushdownProbeTests; the memories side measured the same in
// .claude/context/docs/research/2026-08-21-0017-lance-hybrid-search-semantics/.
file readonly record struct HybridSearchQuery(string Predicate) : IDynamicQuery<HybridSearchArgs, RecordHit> {
    public static CompositeFormat CommandTemplate { get; } = CompositeFormat.Parse(
        """
        SELECT log_position, record_id, stream, category, schema_name, schema_id, schema_format, data, created_at, properties, _hybrid_score
        FROM lance_hybrid_search('ldb.main.records', 'embedding', CAST(? AS FLOAT[]),
                                 'content', ?,
                                 k := ?, alpha := ?,
                                 prefilter := true, refine_factor := 4, oversample_factor := 4)
        {0}
        ORDER BY _hybrid_score DESC
        """);

    public void FormatCommandTemplate(Span<object?> args) => args[0] = Predicate;

    public static StatementBindingResult Bind(in HybridSearchArgs args, PreparedStatement statement) {
        var options = args.Options;
        var index   = 1;

        statement.Bind(index++, options.QueryEmbedding.AsSpan(), CollectionType.List);
        statement.Bind(index++, options.Query);
        statement.Bind(index++, (long)options.K);
        statement.Bind(index++, options.Alpha);

        if (options.Stream is { } stream) statement.Bind(index++, stream);
        if (options.Category is { } category) statement.Bind(index++, category);
        if (options.SchemaName is { } schemaName) statement.Bind(index++, schemaName);
        if (options.SchemaId is { } schemaId) statement.Bind(index++, schemaId);
        if (options.SchemaFormat is { } schemaFormat) statement.Bind(index, schemaFormat);

        return new(statement, completed: true);
    }

    public static RecordHit Parse(ref DataChunk.Row row) =>
        new(RecordReader.Read(ref row), row.ReadFloat());
}

readonly record struct SearchRecordsArgs(string Query, long K);

file struct SearchRecordsQuery : IQuery<SearchRecordsArgs, RecordHit> {
    public static StatementBindingResult Bind(in SearchRecordsArgs args, PreparedStatement statement) =>
        new(statement) {
            args.Query,
            args.K,
        };

    public static ReadOnlySpan<byte> CommandText =>
        """
        SELECT log_position, record_id, stream, category, schema_name, schema_id, schema_format, data, created_at, properties, _score
        FROM lance_fts('ldb.main.records', 'data', ?, k := ?, prefilter := true)
        ORDER BY _score DESC
        """u8;

    public static RecordHit Parse(ref DataChunk.Row row) =>
        new(RecordReader.Read(ref row), row.ReadFloat());
}

readonly record struct GetRecordArgs(Guid RecordId);

file struct GetRecordQuery : IQuery<GetRecordArgs, StoredRecord> {
    // A BLOB binds through the statement; the collection initializer only takes value types.
    public static StatementBindingResult Bind(in GetRecordArgs args, PreparedStatement statement) {
        var recordId = args.RecordId;
        statement.Bind(1, MemoryMarshal.AsBytes(new ReadOnlySpan<Guid>(in recordId)), BlobType.Raw);
        return new(statement, completed: true);
    }

    public static ReadOnlySpan<byte> CommandText =>
        """
        SELECT log_position, record_id, stream, category, schema_name, schema_id, schema_format, data, created_at, properties
        FROM ldb.main.records
        WHERE record_id = ?
        LIMIT 1
        """u8;

    public static StoredRecord Parse(ref DataChunk.Row row) => RecordReader.Read(ref row);
}

readonly record struct GetRecordAtArgs(long LogPosition);

file struct GetRecordAtQuery : IQuery<GetRecordAtArgs, StoredRecord> {
    public static StatementBindingResult Bind(in GetRecordAtArgs args, PreparedStatement statement) =>
        new(statement) { args.LogPosition };

    public static ReadOnlySpan<byte> CommandText =>
        """
        SELECT log_position, record_id, stream, category, schema_name, schema_id, schema_format, data, created_at, properties
        FROM ldb.main.records
        WHERE log_position = ?
        LIMIT 1
        """u8;

    public static StoredRecord Parse(ref DataChunk.Row row) => RecordReader.Read(ref row);
}

static class RecordReader {
    public static StoredRecord Read(ref DataChunk.Row row) =>
        new(
            LogPosition: row.ReadInt64(),
            RecordId: row.TryReadBlob() is { } id ? new Guid(id.Reference.AsSpan()) : Guid.Empty,
            Stream: row.ReadString(),
            Category: row.ReadString(),
            SchemaName: row.ReadString(),
            SchemaId: row.TryReadString(),
            SchemaFormat: row.ReadString(),
            Data: row.TryReadString(),
            CreatedAt: DateTimeOffset.FromUnixTimeMilliseconds(row.ReadInt64()),
            Properties: row.TryReadString());
}
