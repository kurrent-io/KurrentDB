using System.Data.Common;
using DuckDB.NET.Data;

namespace Kurrent.SemanticKernel.Connectors.DuckLance;

/// <summary>
/// EXPERIMENTAL alternative to <see cref="DuckDBConnectionManager"/> that runs a DuckLance store's work against a
/// remote DuckDB server over the beta <c>quack</c> extension (DuckDB v1.5.x) instead of a local connection pool.
/// Built alongside the pool-based manager for side-by-side evaluation; it does not replace it.
/// </summary>
sealed class DuckDBQuackConnectionManager : IDisposable {
    // Experimental — do not use in production. The quack extension is beta and its wire protocol and surface
    // are expected to change (breaking) until at least DuckDB v2.0. On loopback it currently serves plaintext
    // HTTP with a shared-secret token — there is no transport encryption — so this manager is only for
    // local/private evaluation of the remote-server topology, never for untrusted networks.

    // Fixed per-retry backoff (ms) for a transient Lance commit conflict: length (2) plus the initial attempt
    // yields three attempts total, matching DuckDBConnectionManager. Deterministic, no jitter.
    static readonly int[] CommitConflictBackoffMs = [20, 50];

    readonly DuckDBConnection _client;

    // Serializes access to the single, not-thread-safe client connection. Deliberately never disposed: a
    // SemaphoreSlim holds no unmanaged state while its wait handle is unused, and skipping disposal avoids
    // racing any in-flight waiter (mirrors the pool-based manager's admission gate). Genuine concurrency is
    // the SERVER's responsibility, so serializing the single client pipe costs nothing the protocol was
    // relying on the client to provide.
    readonly SemaphoreSlim _gate = new(1, 1);

    readonly string _token;

    // Set by Dispose; guards against issuing work on a disposed client connection.
    volatile bool _disposed;

    public DuckDBQuackConnectionManager(string serverUri, string token) {
        ArgumentException.ThrowIfNullOrEmpty(serverUri);

        ServerUri = serverUri;
        _token    = token;

        // The client is a dumb pipe: an in-memory DuckDB with only the quack extension loaded. All durable state and
        // the lance extension live on the server; the client never attaches anything.
        _client = new("DataSource=:memory:");

        try {
            _client.Open();
            using DbCommand command = _client.CreateCommand();
            command.CommandText = "INSTALL quack; LOAD quack;";
            command.ExecuteNonQuery();
        } catch {
            _client.Dispose();
            throw;
        }
    }

    /// <summary>Gets the quack server URI this manager tunnels through (e.g. <c>quack:localhost</c>).</summary>
    public string ServerUri { get; }

    public void Dispose() {
        _disposed = true;
        _client.Dispose();
    }

    /// <summary>
    /// Tunnels a server-side SQL statement via <c>quack_query</c> and returns every result row as an
    /// ordinal-keyed dictionary (<c>DBNull</c> mapped to <see langword="null"/>).
    /// </summary>
    public Task<IReadOnlyList<Dictionary<string, object?>>> QueryAsync(
        string serverSql,
        IReadOnlyList<object?>? parameters,
        CancellationToken cancellationToken
    ) {
        return RunAsync(
            serverSql, parameters, ReadAllRows,
            cancellationToken);

        // Reads every row of the reader into ordinal-keyed dictionaries, mapping DBNull to null.
        static IReadOnlyList<Dictionary<string, object?>> ReadAllRows(DbDataReader reader) {
            var rows = new List<Dictionary<string, object?>>();

            while (reader.Read()) {
                var row = new Dictionary<string, object?>(reader.FieldCount, StringComparer.Ordinal);

                for (var i = 0; i < reader.FieldCount; i++)
                    row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);

                rows.Add(row);
            }

            return rows;
        }
    }

    /// <summary>
    /// Tunnels a non-<c>SELECT</c> statement (INSERT/UPDATE/DELETE/MERGE/DDL) via <c>quack_query</c> and returns
    /// the server-reported affected-row count (<c>0</c> for DDL).
    /// </summary>
    public Task<long> ExecuteNonQueryAsync(
        string serverSql,
        IReadOnlyList<object?>? parameters,
        CancellationToken cancellationToken
    ) {
        return RunAsync(
            serverSql, parameters, ReadAffectedCount,
            cancellationToken);

        // Reads the affected-row count from a tunneled non-SELECT result. Live-verified result shapes: DML
        // (INSERT/UPDATE/DELETE/MERGE) comes back as a single-row, single-column numeric count (an INSERT as
        // one row with a Count column; a MERGE the same); DDL (CREATE/DROP) comes back as an EMPTY result and
        // therefore reports 0. Some statements return a non-numeric status instead — ALTER INDEX ... OPTIMIZE
        // yields the string "optimize_index" — which means the statement executed and simply has no count to
        // report. In every case the statement DID run on the server.
        static long ReadAffectedCount(DbDataReader reader) {
            if (reader.Read() && reader.FieldCount > 0 && !reader.IsDBNull(0)) {
                var value = reader.GetValue(0);

                return value switch {
                    long l   => l,
                    int i    => i,
                    short s  => s,
                    byte b   => b,
                    ulong ul => (long)ul,
                    uint ui  => ui,
                    _        => 0L
                };
            }

            return 0L;
        }
    }

    /// <summary>
    /// Encodes the parameters into the SQL (when present), then schedules the tunneled query on the thread
    /// pool, serialized on the client connection and retried on a transient commit conflict.
    /// </summary>
    Task<T> RunAsync<T>(
        string serverSql,
        IReadOnlyList<object?>? parameters,
        Func<DbDataReader, T> read,
        CancellationToken cancellationToken
    ) {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return Task.Run(
            function: () => {
                cancellationToken.ThrowIfCancellationRequested();
                ObjectDisposedException.ThrowIf(_disposed, this);

                // Literal-encode outside the gate (it touches no shared state); the fully-formed statement is what
                // the outer quack_query binds as its (parameterized) sql argument. quack_query has NO parameter
                // channel for the remote sql — any '?' inside the tunneled statement is rejected by the server with
                // "Expected N parameters, but none were supplied" — so the '?'-shaped composer SQL is turned into a
                // fully literal statement here (null parameters means the SQL is already a complete literal).
                var remoteSql = parameters is null
                    ? serverSql
                    : DuckDBSqlLiteralEncoder.EncodeInto(serverSql, parameters);

                _gate.Wait(cancellationToken);

                try {
                    return ExecuteWithConflictRetry(() => ExecuteQuackQuery(remoteSql, read));
                } finally {
                    _gate.Release();
                }
            },
            cancellationToken);

        // Runs the operation, retrying a transient Lance commit conflict up to three attempts total.
        // Any other exception, or an exhausted retry budget, propagates. Note the asymmetry with the
        // pool-based manager: its OTHER recovery path — recycling a connection with a stale Lance dataset
        // handle — has no equivalent here, because that stale handle lives on the SERVER's connection to the
        // dataset (which the server opens and re-opens). This client is a stateless pipe with no dataset
        // handle to recycle, so there is nothing on the client side to discard.
        static T ExecuteWithConflictRetry(Func<T> operation) {
            for (var attempt = 0;; attempt++) {
                try {
                    return operation();
                } catch (Exception ex) when (attempt < CommitConflictBackoffMs.Length && IsRetryableCommitConflict(ex)) {
                    // The server instructs a retry; the client pipe is unaffected. Runs on the Task.Run worker thread, so
                    // a blocking sleep is acceptable.
                    Thread.Sleep(CommitConflictBackoffMs[attempt]);
                }
            }
        }

        // Lance's transient optimistic-concurrency commit conflict, surfacing from a server-side write through
        // quack_query as a client exception — the same shape the pool-based manager retries. Matched by Ordinal
        // substring over the full exception text; re-running the same tunneled statement converges.
        static bool IsRetryableCommitConflict(Exception exception) =>
            exception.ToString().Contains("Retryable commit conflict", StringComparison.Ordinal);
    }

    /// <summary>Runs one <c>quack_query</c> round-trip.</summary>
    T ExecuteQuackQuery<T>(string remoteSql, Func<DbDataReader, T> read) {
        // Only the OUTER quack_query(?, ?, token := ?) call is parameterized — uri/sql/token are bound
        // client-side, the one place the protocol accepts parameters. remoteSql is the already-literal
        // statement the server executes.
        using DbCommand command = _client.CreateCommand();
        command.CommandText = "SELECT * FROM quack_query(?, ?, token := ?)";
        command.Parameters.Add(new DuckDBParameter(ServerUri));
        command.Parameters.Add(new DuckDBParameter(remoteSql));
        command.Parameters.Add(new DuckDBParameter(_token));

        using var reader = command.ExecuteReader();
        return read(reader);
    }
}