// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using EventStore.Client;

// A load generator that keeps a ledger of everything the cluster acknowledged.
//
// Each task owns its own streams, so the acked revisions on a stream are monotonic and the
// high-water mark alone is sufficient: if revision R was acked on stream S, then every revision
// up to R must still be readable at the end of the run. That turns verification into one read
// per stream instead of one per event.
//
// Only successful appends go into the ledger. A timed-out or ambiguous append may well have been
// committed, but we make no claim about it - the ledger is deliberately a lower bound on what the
// cluster must still have.

// Under `--scale writer=N` every replica shares the same environment, so fall back to the
// container hostname, which Docker guarantees is unique. Stream names are derived from it.
var writerId = Env("WRITER_ID", Env("HOSTNAME", "1"));
var connection = Env("ESDB_CONNECTION", "esdb://admin:changeit@node1:2113?tls=false");
var concurrency = int.Parse(Env("CONCURRENCY", "16"));
var streamsPerTask = int.Parse(Env("STREAMS_PER_TASK", "4"));
var durationSeconds = int.Parse(Env("DURATION_SECONDS", "600"));
var eventSizeBytes = int.Parse(Env("EVENT_SIZE_BYTES", "128"));
var ledgerDir = Env("LEDGER_DIR", "/ledger");

Console.WriteLine($"writer {writerId}: concurrency={concurrency} streamsPerTask={streamsPerTask} " +
                  $"duration={durationSeconds}s eventSize={eventSizeBytes}B");

var settings = EventStoreClientSettings.Create(connection);
settings.ConnectivitySettings.NodePreference = NodePreference.Leader;
settings.ConnectivitySettings.MaxDiscoverAttempts = 500;
settings.ConnectivitySettings.DiscoveryInterval = TimeSpan.FromMilliseconds(100);
await using var client = new EventStoreClient(settings);

var highWaterMarks = new ConcurrentDictionary<string, long>();
long acked = 0;
long failed = 0;

Directory.CreateDirectory(ledgerDir);
var ledgerPath = Path.Combine(ledgerDir, $"writer-{writerId}.json");
var donePath = Path.Combine(ledgerDir, $"writer-{writerId}.done");
File.Delete(donePath);

using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(durationSeconds));
using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, ctx => {
	ctx.Cancel = true;
	Console.WriteLine($"writer {writerId}: SIGTERM, stopping...");
	cts.Cancel();
});

var runningTime = Stopwatch.StartNew();
var flushing = FlushLedgerPeriodically(cts.Token);
var reporting = ReportPeriodically(cts.Token);
var writing = Enumerable.Range(0, concurrency).Select(t => RunTaskAsync(t, cts.Token)).ToArray();

await Task.WhenAll(writing);
await Task.WhenAll(flushing, reporting);

FlushLedger();
File.WriteAllText(donePath, DateTimeOffset.UtcNow.ToString("O"));

Console.WriteLine($"writer {writerId}: stopped after {runningTime.Elapsed}. " +
                  $"acked={Volatile.Read(ref acked)} failed={Volatile.Read(ref failed)} " +
                  $"streams={highWaterMarks.Count}");
return 0;

async Task RunTaskAsync(int taskIndex, CancellationToken token) {
	var streams = Enumerable.Range(0, streamsPerTask)
		.Select(s => $"w{writerId}-t{taskIndex}-s{s}")
		.ToArray();

	var payload = new byte[eventSizeBytes];
	Random.Shared.NextBytes(payload);

	var next = 0;
	while (!token.IsCancellationRequested) {
		var stream = streams[next++ % streams.Length];
		try {
			var result = await client.AppendToStreamAsync(
				stream,
				StreamState.Any,
				[new EventData(Uuid.NewUuid(), "chaos-event", payload)],
				cancellationToken: token);

			// Only this task writes this stream, so the ack is always the new high-water mark.
			highWaterMarks[stream] = (long)result.NextExpectedStreamRevision.ToUInt64();
			Interlocked.Increment(ref acked);
		} catch (OperationCanceledException) when (token.IsCancellationRequested) {
			return;
		} catch (Exception) {
			// Expected while a leader is being replaced. Back off here and ONLY here - a delay on
			// the success path would silently cap throughput, which is what we need to avoid.
			Interlocked.Increment(ref failed);
			try {
				await Task.Delay(200, token);
			} catch (OperationCanceledException) {
				return;
			}
		}
	}
}

async Task FlushLedgerPeriodically(CancellationToken token) {
	while (!token.IsCancellationRequested) {
		try {
			await Task.Delay(TimeSpan.FromSeconds(2), token);
		} catch (OperationCanceledException) {
			return;
		}

		FlushLedger();
	}
}

void FlushLedger() {
	var ledger = new Ledger(
		WriterId: writerId,
		Acked: Volatile.Read(ref acked),
		Failed: Volatile.Read(ref failed),
		HighWaterMarks: new SortedDictionary<string, long>(highWaterMarks.ToDictionary()));

	// Write-then-rename so the checker never reads a half-written ledger.
	var tmp = ledgerPath + ".tmp";
	File.WriteAllText(tmp, JsonSerializer.Serialize(ledger));
	File.Move(tmp, ledgerPath, overwrite: true);
}

async Task ReportPeriodically(CancellationToken token) {
	long lastAcked = 0;
	var lastAt = TimeSpan.Zero;

	while (!token.IsCancellationRequested) {
		try {
			await Task.Delay(TimeSpan.FromSeconds(5), token);
		} catch (OperationCanceledException) {
			return;
		}

		var now = runningTime.Elapsed;
		var total = Volatile.Read(ref acked);
		var rate = (total - lastAcked) / Math.Max(0.001, (now - lastAt).TotalSeconds);
		lastAcked = total;
		lastAt = now;

		Console.WriteLine($"writer {writerId}: acked={total} failed={Volatile.Read(ref failed)} " +
		                  $"rate={rate:F0}/s @ {now:hh\\:mm\\:ss}");
	}
}

static string Env(string name, string fallback) =>
	Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : fallback;

internal record Ledger(
	string WriterId,
	long Acked,
	long Failed,
	SortedDictionary<string, long> HighWaterMarks);
