using System.Data.Common;
using System.Diagnostics;
using System.Text;
using DuckDB.NET.Data;
using DuckLance.Tests.Support;
using Kurrent.Quack;
using Kurrent.SemanticKernel.Connectors.DuckLance;

namespace DuckLance.Tests.Storage;

/// <summary>
/// Decomposition harness for the per-operation overhead of <see cref="DuckDBConnectionManager"/>, measured against a
/// single temp store holding a canonical seeded Lance table with a plain point-select (<c>SELECT ... WHERE id = ?</c>).
/// </summary>
/// <remarks>
/// <para>
/// The point of this harness is DIAGNOSIS, not assertion: it isolates each additive term in the cost of a small query
/// through the manager and REPORTS the medians (timings are printed, never asserted — the only load-bearing assertions
/// are correctness checks that each measured path returns the one probe row, which keeps this a real <c>[Test]</c>).
/// Everything runs single-threaded. The point-select variants are measured ROUND-ROBIN (one timed iteration of each
/// variant per round) so that JIT warmth, CPU-cache state and thermal drift are shared across variants and cancel out
/// of the deltas — measuring them in separate sequential blocks lets multi-percent drift swamp the tens-of-microseconds
/// terms being hunted. Medians over <see cref="TimedRounds"/> rounds after <see cref="WarmupRounds"/> warmup rounds.
/// </para>
/// <para>
/// The point-select variants, and what each isolates:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <b>(a) RAW floor</b> — one long-lived pooled connection, ONE <see cref="DuckDBCommand"/> created once, its single
/// parameter re-set per iteration, <c>ExecuteReader</c> per iteration. The ADO-level floor for the query.
/// </description></item>
/// <item><description>
/// <b>(a') RAW floor + <c>Prepare()</c></b> — identical to (a) but with <see cref="DbCommand.Prepare"/> called once up
/// front. In DuckDB.NET 1.5.3 <c>DuckDBCommand.Prepare()</c> is an empty method (verified by decompilation), so this is
/// expected to be indistinguishable from (a): direct evidence that command-instance reuse does NOT retain a native
/// prepared statement (H2).
/// </description></item>
/// <item><description>
/// <b>(b) Fresh command</b> — same pooled connection, a NEW <see cref="DuckDBCommand"/> each iteration. Every
/// <c>ExecuteReader</c> in DuckDB.NET re-runs <c>duckdb_extract_statements</c> + <c>duckdb_prepare_extracted_statement</c>
/// + <c>duckdb_execute_prepared</c> + destroy regardless of command instance, so (b) − (a) is only managed
/// command/parameter allocation, NOT SQL parse/plan.
/// </description></item>
/// <item><description>
/// <b>(d) Inline replica</b> — the manager's synchronous shape (admission-semaphore <c>Wait</c>/<c>Release</c> around a
/// fresh-command execute) run inline on the caller thread, WITHOUT <c>Task.Run</c>. The manager's own core is private,
/// so this replicates it; (d) − (b) bounds the admission/wrapper overhead (H3).
/// </description></item>
/// <item><description>
/// <b>(c) Manager path (today/after)</b> — <see cref="DuckDBConnectionManager.ExecuteAsync{T}"/> with a fresh command
/// inside, exactly the production shape. (c) − (d) bounds the manager's async-surface cost (pre-fix: a <c>Task.Run</c>
/// thread hop; H1).
/// </description></item>
/// <item><description>
/// <b>(e) Native prepared reuse (ceiling)</b> — one persistent <see cref="Kurrent.Quack.PreparedStatement"/> (native
/// <c>duckdb_prepare</c>, kept alive across iterations, re-bound per iteration). The ONLY way to genuinely avoid the
/// per-execute re-parse in this stack; the theoretical floor a real prepared-statement cache could reach, quantifying
/// how much parse/plan a cache WOULD save (H2 ceiling). It is NOT reachable through the ADO <see cref="DuckDBCommand"/>
/// surface the manager and its call sites use.
/// </description></item>
/// </list>
/// <para>
/// A second, separate micro-measurement prices the <c>Task.Run</c> hop in ISOLATION (H1): a near-empty
/// <c>ExecuteAsync</c> body — where the query no longer dominates — plus a raw <c>await Task.Run(() =&gt; …)</c> vs an
/// inline call, so the scheduling-hop term is visible above the wall-clock noise floor.
/// </para>
/// </remarks>
[LanceRequired]
[NotInParallel]
public class DuckDBConnectionManagerPerfTests {
    const string Alias         = "perf";
    const string Table         = "perf.main.vs_perf";
    const int    SeedRowCount  = 200;
    const int    WarmupRounds  = 50;
    const int    TimedRounds   = 400;
    const int    HopWarmup     = 200;
    const int    HopIterations = 4000;
    const string ProbeId       = "k100";

    [Test]
    public async Task Decomposition_PointSelectByKey_ReportsManagerOverheadTerms() {
        var dir     = CreateTempStorageDir();
        var manager = new DuckDBConnectionManager(new() { DatabasePath = Path.Combine(dir, "duck.db") }, Alias);

        try {
            // Seed the canonical table through the manager (matches production bootstrap).
            await manager.ExecuteAsync(
                operation: connection => {
                    ExecNonQuery(connection, $"CREATE TABLE {Table} (id VARCHAR, category VARCHAR, content VARCHAR, vec FLOAT[4])");

                    ExecNonQuery(
                        connection,
                        $"INSERT INTO {Table} SELECT 'k' || i, 'cat', 'content ' || i, [0.1, 0.2, 0.3, 0.4]::FLOAT[4] FROM range({SeedRowCount}) t(i)");
                },
                CancellationToken.None);

            // Capture the pooled physical connection: the pool reuses the SAME connection for sequential single-threaded
            // rents (asserted by DuckDBConnectionManagerTests.ExecuteAsync_SequentialCalls_ReuseSamePhysicalConnection),
            // so variants (a),(a'),(b),(d),(e) can drive it directly without renting, isolating them from the async surface.
            DuckDBAdvancedConnection captured = null!;
            await manager.ExecuteAsync(operation: connection => captured = connection, CancellationToken.None);

            var selectSql = $"SELECT id, category, content FROM {Table} WHERE id = ?";

            var decomposition = await MeasurePointSelectDecompositionAsync(manager, captured, selectSql);
            var hop           = await MeasureTaskRunHopAsync(manager);

            // Correctness (the load-bearing assertions): every ADO variant returns exactly the one probe row.
            await Assert.That(decomposition.RowsA).IsEqualTo(1);
            await Assert.That(decomposition.RowsAPrepared).IsEqualTo(1);
            await Assert.That(decomposition.RowsB).IsEqualTo(1);
            await Assert.That(decomposition.RowsD).IsEqualTo(1);
            await Assert.That(decomposition.RowsC).IsEqualTo(1);

            if (decomposition.NativeNote is null)
                await Assert.That(decomposition.RowsE).IsGreaterThanOrEqualTo(1);

            TestContext.Current?.OutputWriter.WriteLine(BuildReport(decomposition, hop));
        } finally {
            manager.Dispose();
            TryDeleteDir(dir);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Point-select decomposition, measured round-robin so drift cancels out of the deltas.
    // ---------------------------------------------------------------------------------------------
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design", "CA1031:Do not catch general exception types",
        Justification =
            "Best-effort ceiling variant (e): any failure to build/validate the native prepared statement is reported as a note, never thrown, so the required (a)-(d) diagnosis still completes.")]
    static async Task<Decomposition> MeasurePointSelectDecompositionAsync(DuckDBConnectionManager manager, DuckDBAdvancedConnection connection, string sql) {
        // Long-lived commands / statement for the reuse variants, created once.
        using var reusedCommand   = CreateProbeCommand(connection, sql, out var reusedParameter);
        using var preparedCommand = CreateProbeCommand(connection, sql, out var preparedParameter);
        preparedCommand.Prepare(); // no-op in DuckDB.NET 1.5.3; measured anyway to prove it changes nothing.

        using var admission = new SemaphoreSlim(32, 32);

        // (e) is best-effort: build AND validate the native prepared statement up front (one full bind/execute/drain);
        // if any part is unavailable, skip it in the loop and report why.
        PreparedStatement nativeStatement   = default;
        var               nativeConstructed = false;
        var               nativeReady       = false;
        string?           nativeNote        = null;

        try {
            nativeStatement   = new(connection, sql.AsSpan());
            nativeConstructed = true;
            _                 = RunPrepared(nativeStatement);
            nativeReady       = true;
        } catch (Exception ex) {
            nativeNote = $"{ex.GetType().Name}: {ex.Message}";
        }

        try {
            var samplesA         = new List<double>(TimedRounds);
            var samplesAPrepared = new List<double>(TimedRounds);
            var samplesB         = new List<double>(TimedRounds);
            var samplesD         = new List<double>(TimedRounds);
            var samplesC         = new List<double>(TimedRounds);
            var samplesE         = new List<double>(TimedRounds);

            int rowsA = 0, rowsAPrepared = 0, rowsB = 0, rowsD = 0, rowsC = 0, rowsE = 0;

            var totalRounds = WarmupRounds + TimedRounds;

            for (var round = 0; round < totalRounds; round++) {
                var timed = round >= WarmupRounds;

                rowsA         = TimeSync(action: () => RunReader(ResetParameter(reusedCommand, reusedParameter)), timed, samplesA);
                rowsAPrepared = TimeSync(action: () => RunReader(ResetParameter(preparedCommand, preparedParameter)), timed, samplesAPrepared);
                rowsB         = TimeSync(action: () => RunFreshCommand(connection, sql), timed, samplesB);
                rowsD         = TimeSync(action: () => RunInlineGated(admission, connection, sql), timed, samplesD);
                rowsC         = await TimeAsync(action: () => ManagerSelectAsync(manager, sql), timed, samplesC);

                if (nativeReady)
                    rowsE = TimeSync(action: () => RunPrepared(nativeStatement), timed, samplesE);
            }

            return new(
                Median(samplesA), Median(samplesAPrepared), Median(samplesB),
                Median(samplesD), Median(samplesC),
                nativeReady ? Median(samplesE) : double.NaN, nativeNote,
                rowsA, rowsAPrepared, rowsB,
                rowsD, rowsC, rowsE);
        } finally {
            if (nativeConstructed)
                nativeStatement.Dispose();
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Task.Run hop in isolation: with a near-empty body the query no longer dominates, so the
    // scheduling hop is visible. Also prices the raw Task.Run mechanism directly.
    // ---------------------------------------------------------------------------------------------
    static async Task<HopMeasurement> MeasureTaskRunHopAsync(DuckDBConnectionManager manager) {
        var managerSamples    = new List<double>(HopIterations);
        var inlineSamples     = new List<double>(HopIterations);
        var rawTaskRunSamples = new List<double>(HopIterations);
        var rawInlineSamples  = new List<double>(HopIterations);

        // Near-empty manager op: the delegate body ignores the connection and returns a constant, so what remains is the
        // per-call surface (pre-fix: Task.Run hop + admission + Rent; post-fix: admission + Rent, inline).
        Task<int> managerNoop() => manager.ExecuteAsync(operation: _ => 0, CancellationToken.None);

        using var admission = new SemaphoreSlim(32, 32);

        var total = HopWarmup + HopIterations;

        for (var i = 0; i < total; i++) {
            var timed = i >= HopWarmup;

            _ = await TimeAsync(managerNoop, timed, managerSamples);
            _ = TimeSync(action: () => InlineNoop(admission), timed, inlineSamples);
            _ = await TimeAsync(action: () => Task.Run(() => 0), timed, rawTaskRunSamples);
            _ = TimeSync(action: static () => 0, timed, rawInlineSamples);
        }

        return new(
            Median(managerSamples), Median(inlineSamples), Median(rawTaskRunSamples),
            Median(rawInlineSamples));
    }

    static int InlineNoop(SemaphoreSlim admission) {
        admission.Wait();

        try {
            return 0;
        } finally {
            admission.Release();
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Timing primitives.
    // ---------------------------------------------------------------------------------------------
    static int TimeSync(Func<int> action, bool timed, List<double> samples) {
        if (!timed)
            return action();

        var stopwatch = Stopwatch.StartNew();
        var result    = action();
        stopwatch.Stop();
        samples.Add(stopwatch.Elapsed.TotalMilliseconds);
        return result;
    }

    static async Task<int> TimeAsync(Func<Task<int>> action, bool timed, List<double> samples) {
        if (!timed)
            return await action();

        var stopwatch = Stopwatch.StartNew();
        var result    = await action();
        stopwatch.Stop();
        samples.Add(stopwatch.Elapsed.TotalMilliseconds);
        return result;
    }

    // ---------------------------------------------------------------------------------------------
    // Operation bodies.
    // ---------------------------------------------------------------------------------------------
    static DbCommand CreateProbeCommand(DuckDBAdvancedConnection connection, string sql, out DuckDBParameter parameter) {
        DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        parameter           = new(ProbeId);
        command.Parameters.Add(parameter);
        return command;
    }

    static DbCommand ResetParameter(DbCommand command, DuckDBParameter parameter) {
        parameter.Value = ProbeId; // re-set per iteration
        return command;
    }

    static int RunInlineGated(SemaphoreSlim admission, DuckDBAdvancedConnection connection, string sql) {
        admission.Wait();

        try {
            return RunFreshCommand(connection, sql);
        } finally {
            admission.Release();
        }
    }

    static Task<int> ManagerSelectAsync(DuckDBConnectionManager manager, string sql) =>
        manager.ExecuteAsync(
            operation: connection => {
                using DbCommand command = connection.CreateCommand();
                command.CommandText = sql;
                command.Parameters.Add(new DuckDBParameter(ProbeId));
                return RunReader(command);
            },
            CancellationToken.None);

    static int RunPrepared(PreparedStatement statement) {
        statement.ClearBindings();
        statement.Bind(1, ProbeId.AsSpan());

        using var result = statement.ExecuteQuery(useStreaming: false);
        var       rows   = 0;

        while (result.TryFetch(out var chunk)) {
            rows++;
            chunk.Dispose();
        }

        return rows;
    }

    static int RunFreshCommand(DuckDBAdvancedConnection connection, string sql) {
        using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.Add(new DuckDBParameter(ProbeId));
        return RunReader(command);
    }

    static int RunReader(DbCommand command) {
        var       rows   = 0;
        using var reader = command.ExecuteReader();

        while (reader.Read()) {
            _ = reader.GetValue(0);
            rows++;
        }

        return rows;
    }

    static void ExecNonQuery(DuckDBAdvancedConnection connection, string sql) {
        using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    // ---------------------------------------------------------------------------------------------
    // Reporting.
    // ---------------------------------------------------------------------------------------------
    static double Median(List<double> samples) {
        List<double> sorted = [.. samples];
        sorted.Sort();
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 0 ? (sorted[mid - 1] + sorted[mid]) / 2d : sorted[mid];
    }

    static string BuildReport(Decomposition d, HopMeasurement h) {
        var builder = new StringBuilder();
        builder.AppendLine();

        builder.AppendLine(
            "[DuckDBConnectionManagerPerfTests] Point-select decomposition (SELECT ... WHERE id = ?), "
          + $"round-robin, N={TimedRounds} timed / {WarmupRounds} warmup rounds, single-threaded, median ms:");

        builder.AppendLine("variant                                      | median (ms) | delta");
        builder.AppendLine("---------------------------------------------|-------------|----------------------------------");
        builder.AppendLine($"(a)  RAW floor: 1 conn, 1 reused command     | {d.MedianA,11:F4} | (floor)");
        builder.AppendLine($"(a') RAW floor + command.Prepare()           | {d.MedianAPrepared,11:F4} | {Delta(d.MedianAPrepared, d.MedianA)} vs (a)  [Prepare() is a no-op]");
        builder.AppendLine($"(b)  Fresh command each iteration            | {d.MedianB,11:F4} | {Delta(d.MedianB, d.MedianA)} vs (a)  [managed alloc, NOT re-parse]");
        builder.AppendLine($"(d)  Inline: admission gate + fresh command  | {d.MedianD,11:F4} | {Delta(d.MedianD, d.MedianB)} vs (b)  [H3 admission/wrapper]");
        builder.AppendLine($"(c)  Manager.ExecuteAsync (production)       | {d.MedianC,11:F4} | {Delta(d.MedianC, d.MedianD)} vs (d)  [H1 async surface]");

        if (d.NativeNote is null)
            builder.AppendLine($"(e)  Native prepared reuse (ceiling)         | {d.MedianE,11:F4} | {Delta(d.MedianE, d.MedianA)} vs (a)  [H2 avoidable parse/plan]");
        else
            builder.AppendLine($"(e)  Native prepared reuse (ceiling)         |         n/a | unavailable: {d.NativeNote}");

        builder.AppendLine();
        builder.AppendLine($"[H1] Task.Run hop in isolation, N={HopIterations} timed / {HopWarmup} warmup, median ms:");
        builder.AppendLine("measurement                                  | median (ms) | delta");
        builder.AppendLine("---------------------------------------------|-------------|----------------------------------");
        builder.AppendLine($"manager ExecuteAsync(_ => 0) (near-empty)    | {h.ManagerNoopMs,11:F4} | (per-call surface)");
        builder.AppendLine($"inline admission gate + return 0             | {h.InlineNoopMs,11:F4} | {Delta(h.ManagerNoopMs, h.InlineNoopMs)} = manager - inline");
        builder.AppendLine($"raw await Task.Run(() => 0)                  | {h.RawTaskRunMs,11:F4} | {Delta(h.RawTaskRunMs, h.RawInlineMs)} vs inline call [pure hop]");
        builder.AppendLine($"raw inline call (baseline)                   | {h.RawInlineMs,11:F4} | (baseline)");

        builder.AppendLine();

        builder.AppendLine(
            "Reading: (a')~(a) and (b)~(a) => DuckDBCommand reuse does NOT retain a native prepared statement (H2 confirmed). "
          + "(a)-(e) is the parse/plan a real prepared-statement cache could reclaim -- unreachable via the ADO command surface. "
          + "The Task.Run hop (H1) is (manager - inline) near-empty and the raw Task.Run delta; against a full point-select it is "
          + "a small fraction of the query execution, which is the dominant term.");

        return builder.ToString();
    }

    static string Delta(double value, double baseline) {
        if (double.IsNaN(value) || double.IsNaN(baseline))
            return "n/a";

        var delta = value - baseline;
        return $"{(delta >= 0 ? "+" : "-")}{Math.Abs(delta):F4} ms";
    }

    static string CreateTempStorageDir() {
        var dir = Path.Combine(Path.GetTempPath(), "ducklance-perf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    static void TryDeleteDir(string dir) {
        try {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        } catch (IOException) {
            // Best-effort cleanup.
        } catch (UnauthorizedAccessException) {
            // Best-effort cleanup.
        }
    }

    readonly record struct Decomposition(
        double MedianA,
        double MedianAPrepared,
        double MedianB,
        double MedianD,
        double MedianC,
        double MedianE,
        string? NativeNote,
        int RowsA,
        int RowsAPrepared,
        int RowsB,
        int RowsD,
        int RowsC,
        int RowsE
    );

    readonly record struct HopMeasurement(
        double ManagerNoopMs,
        double InlineNoopMs,
        double RawTaskRunMs,
        double RawInlineMs
    );
}