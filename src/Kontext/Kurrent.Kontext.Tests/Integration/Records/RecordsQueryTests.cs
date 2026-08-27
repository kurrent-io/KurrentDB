// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;
using EventStore.Plugins.Authorization;
using FluentValidation;
using Kurrent.Kontext.Infrastructure.Validation;
using Kurrent.Kontext.Records;
using Kurrent.Kontext.Records.Data;
using Kurrent.Kontext.Testing;
using KurrentDB.Core;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services.UserManagement;
using KurrentDB.SecondaryIndexing.Query;
using KurrentDB.Testing;
using Microsoft.Extensions.DependencyInjection;
using RecordsContracts = Kurrent.Kontext.Contracts.Records;

namespace Kurrent.Kontext.Tests.Records;

/// <summary>
/// The records `query` operation against a REAL node: the real rewriter, the real to_json wrapper, the
/// real payload expansion, the real authorization provider. None of it is reachable from Kontext's own
/// DuckDB-on-a-temp-dir harness, because the query engine needs the node's bus.
/// </summary>
[Category("Integration")]
[Timeout(300_000)]
public class RecordsQueryTests {
    // Indexing is asynchronous, so a query issued the instant after a write sees nothing. Poll instead
    // of sleeping once: the wait is bounded but its length is not predictable.
    static readonly TimeSpan IndexWait     = TimeSpan.FromSeconds(60);
    static readonly TimeSpan IndexPollGap  = TimeSpan.FromMilliseconds(250);

    [ClassDataSource<NodeShim>(Shared = SharedType.PerTestSession)]
    public required NodeShim Node { get; init; }

    ISystemClient SystemClient => Node.Node.Services.GetRequiredService<ISystemClient>();

    [Test]
    public async ValueTask counts_the_records_it_just_wrote(CancellationToken cancellationToken) {
        // Arrange
        using var dir         = new TempDir();
        using var dataSources = MemorySeeding.NewDataSources(dir.Path);

        await MemorySeeding.CreateSchema(dataSources);

        var expectedCount = 5;
        var stream        = await WriteJsonEvents(expectedCount, cancellationToken);
        var records       = NewRecords(dataSources);

        // Act — the SQL a caller would actually write, against kdb.records.
        var rows = await QueryUntil(
            records,
            $"SELECT count(*) AS n FROM kdb.records WHERE stream = '{stream}'",
            row => row.GetProperty("n").GetInt32() == expectedCount,
            cancellationToken);

        // Assert
        await Assert.That(rows[0].GetProperty("n").GetInt32()).IsEqualTo(expectedCount);
    }

    [Test]
    public async ValueTask reads_fields_out_of_the_json_payload(CancellationToken cancellationToken) {
        // Arrange
        using var dir         = new TempDir();
        using var dataSources = MemorySeeding.NewDataSources(dir.Path);

        await MemorySeeding.CreateSchema(dataSources);

        var expectedCount = 3;
        var stream        = await WriteJsonEvents(expectedCount, cancellationToken);
        var records       = NewRecords(dataSources);

        // Act — `data` carries a JSON record's payload verbatim, so DuckDB's JSON functions reach into
        // it directly. This is the whole reason the operation can answer questions search cannot.
        var rows = await QueryUntil(
            records,
            $"""
             SELECT json_extract_string(data, '$.value') AS value
             FROM kdb.records
             WHERE stream = '{stream}' AND schema_format = 'Json'
             ORDER BY log_position
             """,
            _ => true,
            cancellationToken,
            expectedRows: expectedCount);

        // Assert
        var values = rows.Select(row => row.GetProperty("value").GetString()).ToList();

        await Assert.That(values).IsEquivalentTo(Enumerable.Range(0, expectedCount).Select(i => $"event-{i}").ToList());
    }

    [Test]
    public async ValueTask reports_truncation_when_the_limit_cuts_the_result(CancellationToken cancellationToken) {
        // Arrange
        using var dir         = new TempDir();
        using var dataSources = MemorySeeding.NewDataSources(dir.Path);

        await MemorySeeding.CreateSchema(dataSources);

        var written = 5;
        var limit   = 2;
        var stream  = await WriteJsonEvents(written, cancellationToken);
        var records = NewRecords(dataSources);

        await QueryUntil(
            records,
            $"SELECT count(*) AS n FROM kdb.records WHERE stream = '{stream}'",
            row => row.GetProperty("n").GetInt32() == written,
            cancellationToken);

        // Act
        var response = await records.QueryAsync(
            new() { Sql = $"SELECT log_position FROM kdb.records WHERE stream = '{stream}'", Limit = limit },
            SystemAccounts.System,
            cancellationToken);

        // Assert — an agent that misses this flag reads an incomplete answer as a complete one.
        await Assert.That(response.Rows.Count).IsEqualTo(limit);
        await Assert.That(response.Truncated).IsTrue();
    }

    #region ->> Test Infrastructure <<-

    KontextRecords NewRecords(Kurrent.Kontext.Data.KontextDataSource dataSources) =>
        new(
            new KontextRecordsStore(dataSources),
            KontextTestEmbeddings.Model,
            Validation,
            Node.Node.Services.GetRequiredService<IQueryEngine>(),
            Node.Node.Services.GetRequiredService<IAuthorizationProvider>());

    static readonly RequestValidationService Validation = new(
        new ServiceCollection()
            .AddSingleton<IValidator<RecordsContracts.QueryRequest>, QueryRequestValidator>()
            .AddSingleton<IValidator<RecordsContracts.SearchRequest>, SearchRequestValidator>()
            .BuildServiceProvider());

    async Task<string> WriteJsonEvents(int count, CancellationToken ct) {
        var stream = $"kontext-query-{Guid.NewGuid():N}";
        var events = new Event[count];

        for (var i = 0; i < count; i++) {
            var data = JsonSerializer.SerializeToUtf8Bytes(new { index = i, value = $"event-{i}" });
            events[i] = new Event(Guid.NewGuid(), "KontextQueryTestEvent", isJson: true, data);
        }

        await SystemClient.Writing.WriteEvents(stream, events, requireLeader: false, principal: SystemAccounts.System, cancellationToken: ct);

        return stream;
    }

    /// <summary>
    /// Runs the query until the index has caught up with the writes, or the wait expires. Returns the
    /// rows from the first run that satisfied <paramref name="settled"/>.
    /// </summary>
    async Task<IReadOnlyList<JsonElement>> QueryUntil(
        KontextRecords records,
        string sql,
        Func<JsonElement, bool> settled,
        CancellationToken ct,
        int expectedRows = 1
    ) {
        var clock = Stopwatch.StartNew();

        while (true) {
            var response = await records.QueryAsync(new() { Sql = sql }, SystemAccounts.System, ct);

            var rows = response.Rows
                .Select(row => JsonDocument.Parse(row.ToString()).RootElement)
                .ToList();

            if (rows.Count >= expectedRows && rows.All(settled))
                return rows;

            if (clock.Elapsed > IndexWait)
                throw new TimeoutException($"The index did not catch up within {IndexWait}. Last result: {rows.Count} row(s).");

            await Task.Delay(IndexPollGap, ct);
        }
    }

    #endregion // Test Infrastructure
}
