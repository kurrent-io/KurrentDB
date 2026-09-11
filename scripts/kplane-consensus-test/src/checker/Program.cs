// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics;
using Checker;
using EventStore.Client;

// Owns the verdict for the whole run.
//
// Pumba injects the crashes and freezes but reports nothing about the cluster, so everything that
// decides pass or fail lives here. Compose is wired with --exit-code-from checker, which makes
// this process's exit code the exit code of the run.

var nodes = ParseNodes(Env("NODES", "node1=node1.eventstore:2113,node2=node2.eventstore:2113,node3=node3.eventstore:2113"));
var scheme = Env("SCHEME", "https");
var user = Env("KDB_USER", "admin");
var password = Env("KDB_PASSWORD", "changeit");
var connection = Env("ESDB_CONNECTION", "esdb://admin:changeit@node1.eventstore:2113?tls=true&tlsVerifyCert=false");
var durationSeconds = int.Parse(Env("DURATION_SECONDS", "600"));
var logsDir = Env("LOGS_DIR", "/logs");
var ledgerDir = Env("LEDGER_DIR", "/ledger");
var writerCount = int.Parse(Env("WRITER_COUNT", "1"));
var resignIntervalSeconds = int.Parse(Env("RESIGN_INTERVAL_SECONDS", "45"));
var maxLeaderlessPercent = double.Parse(Env("MAX_LEADERLESS_PERCENT", "15"));
var maxSingleOutageSeconds = double.Parse(Env("MAX_SINGLE_OUTAGE_SECONDS", "45"));
var enforceSlowQueue = bool.Parse(Env("ENFORCE_SLOW_QUEUE", "true"));
var startupTimeoutSeconds = int.Parse(Env("STARTUP_TIMEOUT_SECONDS", "180"));
var settleTimeoutSeconds = int.Parse(Env("SETTLE_TIMEOUT_SECONDS", "120"));
var verifyTimeoutSeconds = int.Parse(Env("VERIFY_TIMEOUT_SECONDS", "300"));

Console.WriteLine("=== KPlane consensus checker ===");
Console.WriteLine($"nodes            : {string.Join(", ", nodes.Select(n => $"{n.Key}={n.Value}"))}");
Console.WriteLine($"duration         : {durationSeconds}s");
Console.WriteLine($"writers          : {writerCount}");
Console.WriteLine($"resign interval  : {(resignIntervalSeconds > 0 ? $"{resignIntervalSeconds}s" : "disabled")}");
Console.WriteLine($"leaderless budget: {maxLeaderlessPercent}%, longest outage {maxSingleOutageSeconds}s");
Console.WriteLine();

var clock = new Stopwatch();
using var probe = new ClusterProbe([.. nodes.Values], scheme, user, password, clock);
using var lifetime = new CancellationTokenSource();

// Wait for the cluster to form BEFORE starting the clock, so that ordinary startup time is not
// charged against the leaderless budget.
if (!await WaitForLeaderAsync(TimeSpan.FromSeconds(startupTimeoutSeconds))) {
	Console.WriteLine($"FAILED: no leader appeared within {startupTimeoutSeconds}s - cluster never formed.");
	return 1;
}

Console.WriteLine($"[run] cluster formed, starting {durationSeconds}s run");
clock.Start();

var polling = probe.PollAsync(TimeSpan.FromMilliseconds(250), lifetime.Token);
var resigning = resignIntervalSeconds > 0
	? probe.ResignLeadersAsync(TimeSpan.FromSeconds(resignIntervalSeconds), lifetime.Token)
	: Task.CompletedTask;
var reporting = ReportPeriodicallyAsync(lifetime.Token);

try {
	await Task.Delay(TimeSpan.FromSeconds(durationSeconds), lifetime.Token);
} catch (OperationCanceledException) {
	// falls through to verification
}

Console.WriteLine();
Console.WriteLine("[run] duration reached, stopping chaos and verifying");
await lifetime.CancelAsync();
await Task.WhenAll(polling, resigning, reporting);
clock.Stop();

await Ledgers.WaitForWritersAsync(ledgerDir, writerCount, TimeSpan.FromSeconds(120), CancellationToken.None);

// Verification reads from the cluster, so give it a leader to read from first.
if (!await WaitForLeaderAsync(TimeSpan.FromSeconds(settleTimeoutSeconds)))
	Console.WriteLine($"WARNING: no leader after {settleTimeoutSeconds}s; verification may be unreliable");

var events = NodeLogs.Read(logsDir);
var ledgers = Ledgers.Read(ledgerDir);
var appointments = Invariants.Appointments(
	events,
	nodes.ToDictionary(n => n.Key, n => Invariants.NormalizeAddress(n.Value)));

Console.WriteLine($"[verify] {events.Count} log events, {ledgers.Count} ledger(s), {appointments.Count} appointment(s)");
Console.WriteLine();

PrintTimeline();
PrintAppointments();

var checks = new List<Check> {
	Invariants.DidSomething(appointments, events),
	Invariants.SingleAppointeePerEpoch(appointments),
	Invariants.EpochsAdvance(appointments),
	Invariants.NoOfflineTruncation(events),
	Invariants.NoLegacyElections(events),
	Invariants.NoUnexpectedCrash(events),
	Invariants.NoSlowQueue(events, enforceSlowQueue),
};

var settings = EventStoreClientSettings.Create(connection);
settings.ConnectivitySettings.NodePreference = NodePreference.Leader;
settings.ConnectivitySettings.MaxDiscoverAttempts = 500;
// Bounded: chaos is still running and the cluster may never recover, but the log-based
// invariants above are already computed and must still be reported.
using var verifyTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(verifyTimeoutSeconds));
await using (var client = new EventStoreClient(settings)) {
	checks.Add(await Invariants.NoAcknowledgedWriteLost(client, ledgers, verifyTimeout.Token));
}

checks.AddRange(Invariants.Availability(
	probe.Timeline,
	clock.Elapsed,
	maxLeaderlessPercent,
	TimeSpan.FromSeconds(maxSingleOutageSeconds)));

return PrintVerdict(checks);

async Task<bool> WaitForLeaderAsync(TimeSpan timeout) {
	var deadline = DateTime.UtcNow + timeout;
	while (DateTime.UtcNow < deadline) {
		var view = await probe.ReadGossipAsync(CancellationToken.None);
		if (view?.Leader is { } leader) {
			Console.WriteLine($"[run] leader is {leader.Address}");
			return true;
		}

		await Task.Delay(TimeSpan.FromSeconds(1));
	}

	return false;
}

async Task ReportPeriodicallyAsync(CancellationToken token) {
	while (!token.IsCancellationRequested) {
		try {
			await Task.Delay(TimeSpan.FromSeconds(30), token);
		} catch (OperationCanceledException) {
			return;
		}

		var timeline = probe.Timeline;
		var changes = timeline.Count(s => s.Leader is not null);
		var outage = timeline.Where(s => s.Leader is null)
			.Aggregate(TimeSpan.Zero, (sum, s) => sum + s.Duration);

		Console.WriteLine(
			$"[run] {clock.Elapsed:hh\\:mm\\:ss} leader={timeline.LastOrDefault()?.Leader ?? "NONE"} " +
			$"leaderships={changes} leaderless={outage.TotalSeconds:F1}s resigns={probe.ResignCount}");
	}
}

void PrintTimeline() {
	var timeline = probe.Timeline;
	Console.WriteLine("Leadership timeline:");
	// Never hide an outage, however brief - a sub-second gap between two terms of the SAME node is
	// a real leadership change and the most interesting kind. Only quiet leader spans are elided.
	foreach (var span in timeline.Where(s => s.Leader is null || s.Duration > TimeSpan.FromMilliseconds(500)))
		Console.WriteLine($"  {span.From:hh\\:mm\\:ss} -> {span.To:hh\\:mm\\:ss} " +
		                  $"({span.Duration.TotalSeconds,6:F1}s) {span.Leader ?? "*** NO LEADER ***"}");

	var distribution = timeline
		.Where(s => s.Leader is not null)
		.GroupBy(s => s.Leader!)
		.Select(g => $"{g.Key}: {g.Sum(s => s.Duration.TotalSeconds):F0}s over {g.Count()} term(s)")
		.ToArray();

	Console.WriteLine($"  distribution: {string.Join(" | ", distribution)}");
	Console.WriteLine();
}

void PrintAppointments() {
	Console.WriteLine("Appointments by epoch:");
	foreach (var group in appointments.GroupBy(a => a.Epoch).OrderBy(g => g.Key).TakeLast(20)) {
		var appointees = string.Join(" and ", group.Select(a => a.Appointee).Distinct());
		Console.WriteLine($"  epoch {group.Key,4}: {appointees} " +
		                  $"(observed by {string.Join(",", group.Select(a => a.ObservedBy).Distinct().Order())})");
	}

	Console.WriteLine();
}

int PrintVerdict(List<Check> results) {
	Console.WriteLine("=== Results ===");
	foreach (var check in results) {
		var mark = check switch {
			{ Warning: true } => "!",
			{ Passed: true } => "PASS",
			_ => "FAIL",
		};
		Console.WriteLine($"  [{mark,4}] {check.Id} {check.Name}: {check.Detail}");
	}

	var failures = results.Count(c => c is { Passed: false });
	Console.WriteLine();
	Console.WriteLine(failures == 0
		? "RUN PASSED"
		: $"RUN FAILED: {failures} invariant(s) violated");

	return failures == 0 ? 0 : 1;
}

static Dictionary<string, string> ParseNodes(string value) =>
	value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
		.Select(pair => pair.Split('=', 2))
		.ToDictionary(parts => parts[0], parts => parts.Length > 1 ? parts[1] : parts[0]);

static string Env(string name, string fallback) =>
	Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : fallback;
