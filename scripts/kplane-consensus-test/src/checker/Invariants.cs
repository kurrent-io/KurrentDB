// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Client;

namespace Checker;

public sealed record Check(string Id, string Name, bool Passed, string Detail, bool Warning = false) {
	public static Check Pass(string id, string name, string detail) => new(id, name, true, detail);
	public static Check Fail(string id, string name, string detail) => new(id, name, false, detail);
	public static Check Warn(string id, string name, string detail) => new(id, name, true, detail, Warning: true);
}

/// <summary>
/// An appointment as some node observed it: which epoch, and who the Kontrol Plane named.
/// </summary>
public sealed record Appointment(DateTimeOffset At, long Epoch, string Appointee, string ObservedBy);

public static class Invariants {
	// Matched against the message TEMPLATE, not the rendered line, so property values never
	// affect the match. Substrings rather than whole templates, so that rewording the tail of a
	// message does not silently switch a check off.
	private const string AppointedOther = "Kontrol plane appointed [{leaderAddress}]";
	private const string AppointedSelf = "Kontrol plane appointed this node as leader";
	private const string OfflineTruncation = "OFFLINE TRUNCATION IS NEEDED";
	private const string SlowQueue = "VERY SLOW QUEUE MSG";
	private const string LegacyElections = "ELECTIONS:";
	private const string UnhandledException = "Global Unhandled Exception";
	private const string Freezing = "IS FREEZING";

	public static IReadOnlyList<Appointment> Appointments(
		IEnumerable<LogEvent> events,
		IReadOnlyDictionary<string, string> nodeAddresses) {

		var appointments = new List<Appointment>();

		foreach (var e in events) {
			string? appointee = null;

			if (e.TemplateContains(AppointedOther))
				appointee = e.String("leaderAddress");
			else if (e.TemplateContains(AppointedSelf))
				// The self form names no address - the appointee is whoever wrote the log.
				appointee = nodeAddresses.GetValueOrDefault(e.Node, e.Node);

			if (appointee is null || e.Number("epoch") is not { } epoch)
				continue;

			appointments.Add(new Appointment(e.Timestamp, epoch, NormalizeAddress(appointee), e.Node));
		}

		return appointments;
	}

	/// <summary>
	/// S1: an epoch belongs to exactly one leader. This is the invariant fencing exists to
	/// protect - two nodes appointed at the same epoch is a split brain.
	/// </summary>
	public static Check SingleAppointeePerEpoch(IReadOnlyList<Appointment> appointments) {
		var conflicts = appointments
			.GroupBy(a => a.Epoch)
			.Select(g => (Epoch: g.Key, Appointees: g.Select(a => a.Appointee).Distinct().ToArray()))
			.Where(x => x.Appointees.Length > 1)
			.ToArray();

		if (conflicts.Length == 0)
			return Check.Pass("S1", "One appointee per epoch",
				$"{appointments.Select(a => a.Epoch).Distinct().Count()} epochs, no conflicts");

		var detail = string.Join("; ", conflicts.Select(c =>
			$"epoch {c.Epoch} appointed to {string.Join(" and ", c.Appointees)}"));

		return Check.Fail("S1", "One appointee per epoch", $"SPLIT BRAIN: {detail}");
	}

	/// <summary>
	/// S2: epochs move forwards. Compared by each epoch's FIRST sighting anywhere, because nodes
	/// learn of an appointment at slightly different moments and their logs interleave.
	/// </summary>
	public static Check EpochsAdvance(IReadOnlyList<Appointment> appointments) {
		var firstSeen = appointments
			.GroupBy(a => a.Epoch)
			.Select(g => (Epoch: g.Key, At: g.Min(a => a.At)))
			.OrderBy(x => x.Epoch)
			.ToArray();

		var regressions = new List<string>();
		for (var i = 1; i < firstSeen.Length; i++) {
			if (firstSeen[i].At < firstSeen[i - 1].At)
				regressions.Add(
					$"epoch {firstSeen[i].Epoch} first seen at {firstSeen[i].At:HH:mm:ss.fff}, " +
					$"before epoch {firstSeen[i - 1].Epoch} at {firstSeen[i - 1].At:HH:mm:ss.fff}");
		}

		return regressions.Count == 0
			? Check.Pass("S2", "Epochs advance",
				firstSeen.Length > 0
					? $"epochs {firstSeen[0].Epoch}..{firstSeen[^1].Epoch}, monotonic"
					: "no epochs observed")
			: Check.Fail("S2", "Epochs advance", $"EPOCH WENT BACKWARDS: {string.Join("; ", regressions)}");
	}

	/// <summary>
	/// S3: offline truncation means the node found committed data it had to discard - the
	/// clearest signal that the fence let something through.
	/// </summary>
	public static Check NoOfflineTruncation(IReadOnlyList<LogEvent> events) {
		var hits = events.Where(e => e.TemplateContains(OfflineTruncation)).ToArray();
		if (hits.Length == 0)
			return Check.Pass("S3", "No offline truncation", "none");

		var detail = string.Join("; ", hits.Take(5).Select(e => $"{e.Node} @ {e.Timestamp:HH:mm:ss}"));
		return Check.Fail("S3", "No offline truncation", $"{hits.Length} occurrence(s): {detail}");
	}

	/// <summary>
	/// S5: in Kontrol Plane mode the elections service is never constructed, so a single
	/// ELECTIONS line means the cluster is not running the code path under test.
	/// </summary>
	public static Check NoLegacyElections(IReadOnlyList<LogEvent> events) {
		var hits = events.Where(e => e.TemplateContains(LegacyElections)).ToArray();
		return hits.Length == 0
			? Check.Pass("S5", "No legacy elections", "none")
			: Check.Fail("S5", "No legacy elections",
				$"{hits.Length} election log line(s) - cluster is NOT in Kontrol Plane mode, " +
				$"e.g. {hits[0].Node}: {hits[0].Template}");
	}

	/// <summary>
	/// S6: a node killed by Pumba dies silently, so any fatal or unhandled exception in the logs
	/// is a crash the harness did not ask for.
	/// </summary>
	public static Check NoUnexpectedCrash(IReadOnlyList<LogEvent> events) {
		var hits = events
			.Where(e => e.Level is "Fatal" ||
			            (e.Level is "Error" && e.TemplateContains(UnhandledException)))
			.ToArray();

		if (hits.Length == 0)
			return Check.Pass("S6", "No unexpected crash", "no fatal or unhandled exceptions");

		var detail = string.Join("; ", hits.Take(5).Select(e =>
			$"{e.Node} @ {e.Timestamp:HH:mm:ss}: {Truncate(e.Template, 120)}"));

		return Check.Fail("S6", "No unexpected crash", $"{hits.Length} fatal event(s): {detail}");
	}

	/// <summary>
	/// S7: a stalled queue under chaos is usually a real problem, but it is also the first thing
	/// to appear when the box is simply overloaded - hence the toggle.
	/// </summary>
	public static Check NoSlowQueue(IReadOnlyList<LogEvent> events, bool enforce) {
		var hits = events.Where(e => e.TemplateContains(SlowQueue)).ToArray();
		if (hits.Length == 0)
			return Check.Pass("S7", "No very slow queue", "none");

		var detail = $"{hits.Length} occurrence(s), first on {hits[0].Node} @ {hits[0].Timestamp:HH:mm:ss}";
		return enforce
			? Check.Fail("S7", "No very slow queue", detail)
			: Check.Warn("S7", "No very slow queue", $"{detail} (not enforced)");
	}

	/// <summary>
	/// S4: every append the cluster acknowledged must still be readable. The ledger records a
	/// high-water mark per stream, so one read per stream settles it.
	/// </summary>
	public static async Task<Check> NoAcknowledgedWriteLost(
		EventStoreClient client,
		IReadOnlyList<Ledger> ledgers,
		CancellationToken token) {

		if (ledgers.Count == 0)
			return Check.Fail("S4", "No acknowledged write lost", "no writer ledgers found");

		var losses = new List<string>();
		var streams = 0;
		var verified = 0;
		long acked = 0;

		// Chaos keeps running while we verify - Pumba has no shell, so it cannot be told to stop
		// before this point. If the cluster never regains quorum we must still report, rather than
		// throw away the whole run's findings on an unreadable stream.
		try {
			foreach (var ledger in ledgers) {
				acked += ledger.Acked;
				foreach (var (stream, expected) in ledger.HighWaterMarks) {
					streams++;
					var actual = await LastRevisionAsync(client, stream, token);
					verified++;

					if (actual is null)
						losses.Add($"{stream}: stream missing, expected revision >= {expected}");
					else if (actual < expected)
						losses.Add($"{stream}: last revision {actual}, expected >= {expected}");
				}
			}
		} catch (Exception ex) {
			return Check.Fail("S4", "No acknowledged write lost",
				$"INCONCLUSIVE after {verified}/{streams} streams - the cluster could not be read " +
				$"({ex.GetType().Name}: {ex.Message}). " +
				(losses.Count > 0 ? $"Losses found so far: {string.Join("; ", losses.Take(5))}" : "No losses found so far."));
		}

		return losses.Count == 0
			? Check.Pass("S4", "No acknowledged write lost",
				$"{acked} acked appends across {streams} streams all still readable")
			: Check.Fail("S4", "No acknowledged write lost",
				$"DATA LOSS on {losses.Count}/{streams} streams: {string.Join("; ", losses.Take(5))}");
	}

	private static async Task<long?> LastRevisionAsync(
		EventStoreClient client, string stream, CancellationToken token) {

		for (var attempt = 0; attempt < 30; attempt++) {
			try {
				var result = client.ReadStreamAsync(
					Direction.Backwards, stream, StreamPosition.End, maxCount: 1, cancellationToken: token);

				if (await result.ReadState == ReadState.StreamNotFound)
					return null;

				await foreach (var e in result.WithCancellation(token))
					return (long)e.Event.EventNumber.ToUInt64();

				return null;
			} catch (Exception) when (attempt < 29) {
				// The cluster may still be settling after the last injected fault.
				await Task.Delay(TimeSpan.FromSeconds(1), token);
			}
		}

		throw new InvalidOperationException($"could not read stream {stream} to verify the ledger");
	}

	/// <summary>
	/// L1/L2: availability. Some leaderless time is expected - every fault causes some - so these
	/// are budgets, not absolutes.
	/// </summary>
	public static IEnumerable<Check> Availability(
		IReadOnlyList<LeadershipSpan> timeline,
		TimeSpan runtime,
		double maxLeaderlessPercent,
		TimeSpan maxSingleOutage) {

		var outages = timeline.Where(s => s.Leader is null).ToArray();
		var total = outages.Aggregate(TimeSpan.Zero, (sum, s) => sum + s.Duration);
		var percent = runtime > TimeSpan.Zero ? total.TotalSeconds / runtime.TotalSeconds * 100 : 0;

		yield return percent <= maxLeaderlessPercent
			? Check.Pass("L1", "Leaderless budget",
				$"{total.TotalSeconds:F1}s leaderless of {runtime.TotalSeconds:F0}s ({percent:F1}%, budget {maxLeaderlessPercent}%)")
			: Check.Fail("L1", "Leaderless budget",
				$"{total.TotalSeconds:F1}s leaderless ({percent:F1}%) exceeds budget of {maxLeaderlessPercent}%");

		var longest = outages.Length > 0 ? outages.Max(s => s.Duration) : TimeSpan.Zero;

		yield return longest <= maxSingleOutage
			? Check.Pass("L2", "Longest outage",
				$"{longest.TotalSeconds:F1}s (limit {maxSingleOutage.TotalSeconds:F0}s)")
			: Check.Fail("L2", "Longest outage",
				$"{longest.TotalSeconds:F1}s exceeds limit of {maxSingleOutage.TotalSeconds:F0}s");
	}

	/// <summary>
	/// A run in which no leadership ever changed proves nothing, so treat it as a failure of the
	/// harness rather than a clean pass.
	/// </summary>
	public static Check DidSomething(IReadOnlyList<Appointment> appointments, IReadOnlyList<LogEvent> events) {
		var epochs = appointments.Select(a => a.Epoch).Distinct().Count();
		var freezes = events.Count(e => e.TemplateContains(Freezing));

		return epochs >= 2
			? Check.Pass("A0", "Chaos actually happened",
				$"{epochs} distinct epochs appointed, {freezes} freeze(s)")
			: Check.Fail("A0", "Chaos actually happened",
				$"only {epochs} epoch(s) appointed - no leadership change was exercised, " +
				$"so the run proves nothing");
	}

	/// <summary>
	/// Serilog renders an <see cref="System.Net.EndPoint"/> with ToString(), and DnsEndPoint
	/// prefixes its address family - "Unspecified/node1.eventstore:2113". The self form of the
	/// appointment message names no address at all, so that one comes from the configured node
	/// map instead. Both have to reduce to the same string or one appointment observed by two
	/// nodes reads as two appointees.
	/// </summary>
	internal static string NormalizeAddress(string address) {
		var slash = address.LastIndexOf('/');
		var trimmed = slash >= 0 ? address[(slash + 1)..] : address;
		return trimmed.Trim().ToLowerInvariant();
	}

	private static string Truncate(string value, int max) =>
		value.Length <= max ? value : value[..max] + "...";
}
