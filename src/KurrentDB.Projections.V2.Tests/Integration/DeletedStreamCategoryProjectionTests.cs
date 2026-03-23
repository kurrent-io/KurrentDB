// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text;
using EventStore.Client.Projections;
using EventStore.Client.Streams;
using Google.Protobuf;
using Grpc.Core;
using KurrentDB.Projections.V2.Tests.Fixtures;
using KurrentDB.Protocol.V2.Streams;
using SchemaFormat = KurrentDB.Protocol.V2.Streams.SchemaFormat;
using SchemaInfo = KurrentDB.Protocol.V2.Streams.SchemaInfo;

namespace KurrentDB.Projections.V2.Tests.Integration;

/// <summary>
/// Tests that V2 projections handle $deleted notifications with fromCategory().foreachStream().
/// Mirrors the V1 tests in KurrentDB.Projections.Core.Tests/ClientAPI/when_handling_deleted/with_from_category_foreach_projection/.
/// </summary>
[NotInParallel]
public class DeletedStreamCategoryProjectionTests {
	[ClassDataSource<ProjectionsNodeFixture>(Shared = SharedType.PerTestSession)]
	public required ProjectionsNodeFixture Fixture { get; init; }

	#region Helpers

	/// <summary>
	/// Builds a fromCategory projection source with incrementing counter.
	/// </summary>
	static string IncrementSource(string category) => $$"""
		fromCategory('{{category}}').foreachStream().when({
			$init: function() { return { a: 0 } },
			type1: function(s, e) { s.a++; return s; },
			type2: function(s, e) { s.a++; return s; },
			$deleted: function(s, e) { s.deleted = 1; return s; },
		}).outputState();
		""";

	/// <summary>
	/// Builds a fromCategory projection source with set-to-1 semantics.
	/// </summary>
	static string SetSource(string category) => $$"""
		fromCategory('{{category}}').foreachStream().when({
			$init: function() { return {} },
			type1: function(s, e) { s.a = 1; return s; },
			type2: function(s, e) { s.a = 1; return s; },
			$deleted: function(s, e) { s.deleted = 1; return s; },
		}).outputState();
		""";

	async Task AppendEvent(string stream, string eventType, CancellationToken ct) {
		await Fixture.StreamsClient.AppendAsync(new AppendRequest {
			Stream = stream,
			ExpectedRevision = -2, // Any
			Records = {
				new AppendRecord {
					RecordId = Guid.NewGuid().ToString(),
					Data = ByteString.CopyFromUtf8("{}"),
					Schema = new SchemaInfo { Name = eventType, Format = SchemaFormat.Json }
				}
			}
		}, cancellationToken: ct);
	}

	async Task HardDeleteStream(string stream, CancellationToken ct) {
		await Fixture.V1StreamsClient.TombstoneAsync(new TombstoneReq {
			Options = new TombstoneReq.Types.Options {
				StreamIdentifier = new StreamIdentifier {
					StreamName = ByteString.CopyFromUtf8(stream)
				},
				Any = new Empty()
			}
		}, cancellationToken: ct);
	}

	async Task<string> CreateProjection(string source, int engineVersion, CancellationToken ct) {
		var testId = Guid.NewGuid().ToString("N")[..8];
		var name = $"catdel-v{engineVersion}-{testId}";
		await Fixture.ProjectionsClient.CreateAsync(new CreateReq {
			Options = new CreateReq.Types.Options {
				Continuous = new CreateReq.Types.Options.Types.Continuous {
					Name = name,
					EmitEnabled = true,
					TrackEmittedStreams = false
				},
				Query = source,
				EngineVersion = engineVersion
			}
		}, cancellationToken: ct);
		return name;
	}

	async Task WaitForProjectionStatus(string name, string expectedStatus, TimeSpan timeout, CancellationToken ct) {
		var deadline = DateTime.UtcNow + timeout;
		string lastStatus = "unknown";
		while (DateTime.UtcNow < deadline) {
			ct.ThrowIfCancellationRequested();
			try {
				var stats = Fixture.ProjectionsClient.Statistics(new StatisticsReq {
					Options = new StatisticsReq.Types.Options { Name = name }
				}, cancellationToken: ct);
				await foreach (var stat in stats.ResponseStream.ReadAllAsync(ct)) {
					lastStatus = stat.Details.Status;
					if (stat.Details.Status.Contains(expectedStatus, StringComparison.OrdinalIgnoreCase))
						return;
				}
			} catch (RpcException) { }
			await Task.Delay(500, ct);
		}
		throw new TimeoutException($"Projection '{name}' did not reach status '{expectedStatus}' within {timeout}. Last status: '{lastStatus}'");
	}

	async Task WaitForEventsProcessed(string name, int minEvents, TimeSpan timeout, CancellationToken ct) {
		var deadline = DateTime.UtcNow + timeout;
		long lastCount = 0;
		while (DateTime.UtcNow < deadline) {
			ct.ThrowIfCancellationRequested();
			try {
				var stats = Fixture.ProjectionsClient.Statistics(new StatisticsReq {
					Options = new StatisticsReq.Types.Options { Name = name }
				}, cancellationToken: ct);
				await foreach (var stat in stats.ResponseStream.ReadAllAsync(ct)) {
					lastCount = stat.Details.EventsProcessedAfterRestart;
					if (lastCount >= minEvents)
						return;
				}
			} catch (RpcException) { }
			await Task.Delay(500, ct);
		}
		throw new TimeoutException($"Projection '{name}' processed only {lastCount}/{minEvents} events within {timeout}");
	}

	async Task DisableProjection(string name, CancellationToken ct) {
		await Fixture.ProjectionsClient.DisableAsync(new DisableReq {
			Options = new DisableReq.Types.Options { Name = name, WriteCheckpoint = true }
		}, cancellationToken: ct);
	}

	async Task EnableProjection(string name, CancellationToken ct) {
		await Fixture.ProjectionsClient.EnableAsync(new EnableReq {
			Options = new EnableReq.Types.Options { Name = name }
		}, cancellationToken: ct);
	}

	async Task FlushProjectionResults(string name, CancellationToken ct) {
		await DisableProjection(name, ct);
		await WaitForProjectionStatus(name, "Stopped", TimeSpan.FromSeconds(30), ct);
		await Task.Delay(1000, ct);
		await EnableProjection(name, ct);
		await WaitForProjectionStatus(name, "Running", TimeSpan.FromSeconds(30), ct);
	}

	async Task<string?> ReadLastEventData(string stream, CancellationToken ct) {
		try {
			var request = new ReadReq {
				Options = new ReadReq.Types.Options {
					Stream = new ReadReq.Types.Options.Types.StreamOptions {
						StreamIdentifier = new StreamIdentifier {
							StreamName = ByteString.CopyFromUtf8(stream)
						},
						End = new Empty()
					},
					ReadDirection = ReadReq.Types.Options.Types.ReadDirection.Backwards,
					ResolveLinks = false,
					Count = 1,
					UuidOption = new ReadReq.Types.Options.Types.UUIDOption { String = new Empty() },
					NoFilter = new Empty(),
					ControlOption = new ReadReq.Types.Options.Types.ControlOption { Compatibility = 1 }
				}
			};

			using var call = Fixture.V1StreamsClient.Read(request, cancellationToken: ct);
			await foreach (var response in call.ResponseStream.ReadAllAsync(ct)) {
				if (response.Event is { Event: { } evt })
					return Encoding.UTF8.GetString(evt.Data.Span);
			}
		} catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound) {
			// Stream doesn't exist
		}
		return null;
	}

	async Task AssertResultStreamTail(string resultStream, string expectedJson, CancellationToken ct) {
		var data = await ReadLastEventData(resultStream, ct);
		await Assert.That(data).IsNotNull().Because($"Expected result in '{resultStream}' but stream is empty or doesn't exist");
		await Assert.That(data!).IsEqualTo(expectedJson).Because($"Unexpected state in '{resultStream}'");
	}

	#endregion

	#region Tests — V2 Engine: fromCategory().foreachStream() with $deleted

	/// <summary>
	/// Events written and indexed, stream hard-deleted, then projection created.
	/// Mirrors: when_running_and_events_are_indexed_but_tombstone
	/// </summary>
	[Test]
	[Timeout(180_000)]
	public async Task v2_category_tombstone_after_events_indexed(CancellationToken ct) {
		var category = $"catdela{Guid.NewGuid():N}";
		var stream1 = $"{category}-1";
		var stream2 = $"{category}-2";

		// 1. Write events to both streams
		await AppendEvent(stream1, "type1", ct);
		await AppendEvent(stream1, "type2", ct);
		await AppendEvent(stream2, "type1", ct);
		await AppendEvent(stream2, "type2", ct);
		await Task.Delay(2000, ct);

		// 2. Hard delete stream-1
		await HardDeleteStream(stream1, ct);
		await Task.Delay(2000, ct);

		// 3. Create projection and wait for it to process the events
		var name = await CreateProjection(SetSource(category), engineVersion: 2, ct);
		await WaitForProjectionStatus(name, "Running", TimeSpan.FromSeconds(30), ct);
		await WaitForEventsProcessed(name, 4, TimeSpan.FromSeconds(30), ct);

		// 4. Flush results
		await FlushProjectionResults(name, ct);
		await Task.Delay(2000, ct);

		// 5. Verify: stream-1 should have a=1,deleted=1; stream-2 should have a=1
		var stream1Result = $"$projections-{name}-{stream1}-result";
		var stream2Result = $"$projections-{name}-{stream2}-result";
		await AssertResultStreamTail(stream1Result, """{"a":1,"deleted":1}""", ct);
		await AssertResultStreamTail(stream2Result, """{"a":1}""", ct);
	}

	/// <summary>
	/// Events written, projection running, then stream hard-deleted.
	/// Mirrors: when_running_and_no_indexing (adapted — V2 reads $all so no indexing distinction)
	/// </summary>
	[Test]
	[Timeout(180_000)]
	public async Task v2_category_tombstone_while_projection_running(CancellationToken ct) {
		var category = $"catdelb{Guid.NewGuid():N}";
		var stream1 = $"{category}-1";
		var stream2 = $"{category}-2";

		// 1. Write events
		await AppendEvent(stream1, "type1", ct);
		await AppendEvent(stream1, "type2", ct);
		await AppendEvent(stream2, "type1", ct);
		await AppendEvent(stream2, "type2", ct);
		await Task.Delay(2000, ct);

		// 2. Create and start projection
		var name = await CreateProjection(SetSource(category), engineVersion: 2, ct);
		await WaitForProjectionStatus(name, "Running", TimeSpan.FromSeconds(30), ct);
		await WaitForEventsProcessed(name, 4, TimeSpan.FromSeconds(30), ct);

		// 3. Hard delete stream-1
		await HardDeleteStream(stream1, ct);
		await Task.Delay(3000, ct);

		// 4. Flush to force results to be written
		await FlushProjectionResults(name, ct);
		await Task.Delay(2000, ct);

		// 5. Verify
		var stream1Result = $"$projections-{name}-{stream1}-result";
		var stream2Result = $"$projections-{name}-{stream2}-result";
		await AssertResultStreamTail(stream1Result, """{"a":1,"deleted":1}""", ct);
		await AssertResultStreamTail(stream2Result, """{"a":1}""", ct);
	}

	/// <summary>
	/// Events written, projection running, stream hard-deleted, then more events to other streams.
	/// Mirrors: when_running_and_then_other_events_tombstone_ant_other_events
	/// </summary>
	[Test]
	[Timeout(180_000)]
	public async Task v2_category_tombstone_while_running_with_more_events(CancellationToken ct) {
		var category = $"catdelc{Guid.NewGuid():N}";
		var stream1 = $"{category}-1";
		var stream2 = $"{category}-2";
		var stream3 = $"{category}-3";

		// 1. Write events
		await AppendEvent(stream1, "type1", ct);
		await AppendEvent(stream1, "type2", ct);
		await AppendEvent(stream2, "type1", ct);
		await AppendEvent(stream2, "type2", ct);
		await Task.Delay(2000, ct);

		// 2. Create projection
		var name = await CreateProjection(IncrementSource(category), engineVersion: 2, ct);
		await WaitForProjectionStatus(name, "Running", TimeSpan.FromSeconds(30), ct);
		await WaitForEventsProcessed(name, 4, TimeSpan.FromSeconds(30), ct);

		// 3. Hard delete stream-1
		await HardDeleteStream(stream1, ct);
		await Task.Delay(2000, ct);

		// 4. Write more events
		await AppendEvent(stream2, "type1", ct);
		await AppendEvent(stream2, "type2", ct);
		await AppendEvent(stream3, "type1", ct);

		// 5. Wait for new events to be processed, then flush
		await WaitForEventsProcessed(name, 7, TimeSpan.FromSeconds(30), ct);
		await FlushProjectionResults(name, ct);
		await Task.Delay(2000, ct);

		// 6. Verify
		var stream1Result = $"$projections-{name}-{stream1}-result";
		var stream2Result = $"$projections-{name}-{stream2}-result";
		var stream3Result = $"$projections-{name}-{stream3}-result";
		await AssertResultStreamTail(stream1Result, """{"a":2,"deleted":1}""", ct);
		await AssertResultStreamTail(stream2Result, """{"a":4}""", ct);
		await AssertResultStreamTail(stream3Result, """{"a":1}""", ct);
	}

	/// <summary>
	/// Some events indexed, then new events + tombstone for a stream that wasn't indexed yet.
	/// Mirrors: when_running_and_events_are_indexed_but_a_stream_and_tombstone_postponed
	/// </summary>
	[Test]
	[Timeout(180_000)]
	public async Task v2_category_tombstone_for_stream_with_events_written_after_indexing(CancellationToken ct) {
		var category = $"catdeld{Guid.NewGuid():N}";
		var stream1 = $"{category}-1";
		var stream2 = $"{category}-2";

		// 1. Write events to stream-2 first (indexed)
		await AppendEvent(stream2, "type1", ct);
		await AppendEvent(stream2, "type2", ct);
		await Task.Delay(2000, ct);

		// 2. Write events to stream-1 and tombstone it
		await AppendEvent(stream1, "type1", ct);
		await AppendEvent(stream1, "type2", ct);
		await HardDeleteStream(stream1, ct);
		await Task.Delay(2000, ct);

		// 3. Create projection
		var name = await CreateProjection(IncrementSource(category), engineVersion: 2, ct);
		await WaitForProjectionStatus(name, "Running", TimeSpan.FromSeconds(30), ct);
		await WaitForEventsProcessed(name, 4, TimeSpan.FromSeconds(30), ct);

		// 4. Flush and verify
		await FlushProjectionResults(name, ct);
		await Task.Delay(2000, ct);

		var stream1Result = $"$projections-{name}-{stream1}-result";
		await AssertResultStreamTail(stream1Result, """{"a":2,"deleted":1}""", ct);
	}

	#endregion

	#region Tests — V2 Engine: Recovery with fromCategory and $deleted

	/// <summary>
	/// Projection processes events including tombstone, stops, then recovers.
	/// Mirrors: recovery/when_running_and_events_get_indexed_before_recovery
	/// </summary>
	[Test]
	[Timeout(180_000)]
	public async Task v2_category_recovery_after_tombstone(CancellationToken ct) {
		var category = $"catdelf{Guid.NewGuid():N}";
		var stream1 = $"{category}-1";
		var stream2 = $"{category}-2";

		// 1. Write events
		await AppendEvent(stream1, "type1", ct);
		await AppendEvent(stream2, "type1", ct);
		await AppendEvent(stream1, "type2", ct);
		await AppendEvent(stream2, "type2", ct);
		await Task.Delay(2000, ct);

		// 2. Create projection, wait for events, then tombstone
		var name = await CreateProjection(IncrementSource(category), engineVersion: 2, ct);
		await WaitForProjectionStatus(name, "Running", TimeSpan.FromSeconds(30), ct);
		await WaitForEventsProcessed(name, 4, TimeSpan.FromSeconds(30), ct);

		// 3. Hard delete stream-1
		await HardDeleteStream(stream1, ct);
		await Task.Delay(3000, ct);

		// 4. Stop projection (forces checkpoint/result write)
		await DisableProjection(name, ct);
		await WaitForProjectionStatus(name, "Stopped", TimeSpan.FromSeconds(30), ct);
		await Task.Delay(2000, ct);

		// 5. Re-enable projection (recovery)
		await EnableProjection(name, ct);
		await WaitForProjectionStatus(name, "Running", TimeSpan.FromSeconds(30), ct);
		await Task.Delay(5000, ct);

		// 6. Flush again to ensure results are written after recovery
		await FlushProjectionResults(name, ct);
		await Task.Delay(2000, ct);

		// 7. Verify state is preserved after recovery
		var stream1Result = $"$projections-{name}-{stream1}-result";
		var stream2Result = $"$projections-{name}-{stream2}-result";
		await AssertResultStreamTail(stream1Result, """{"a":2,"deleted":1}""", ct);
		await AssertResultStreamTail(stream2Result, """{"a":2}""", ct);
	}

	/// <summary>
	/// Projection running, stops, tombstone happens while stopped, then recovers.
	/// Mirrors: recovery/when_running_and_events_are_indexed (abort + re-enable)
	/// </summary>
	[Test]
	[Timeout(180_000)]
	public async Task v2_category_recovery_tombstone_while_stopped(CancellationToken ct) {
		var category = $"catdelg{Guid.NewGuid():N}";
		var stream1 = $"{category}-1";
		var stream2 = $"{category}-2";

		// 1. Write events
		await AppendEvent(stream1, "type1", ct);
		await AppendEvent(stream1, "type2", ct);
		await AppendEvent(stream2, "type1", ct);
		await AppendEvent(stream2, "type2", ct);
		await Task.Delay(2000, ct);

		// 2. Create projection and wait for processing
		var name = await CreateProjection(IncrementSource(category), engineVersion: 2, ct);
		await WaitForProjectionStatus(name, "Running", TimeSpan.FromSeconds(30), ct);
		await WaitForEventsProcessed(name, 4, TimeSpan.FromSeconds(30), ct);

		// 3. Stop projection (forces checkpoint/result write)
		await DisableProjection(name, ct);
		await WaitForProjectionStatus(name, "Stopped", TimeSpan.FromSeconds(30), ct);
		await Task.Delay(2000, ct);

		// 4. Hard delete stream-1 while projection is stopped
		await HardDeleteStream(stream1, ct);
		await Task.Delay(2000, ct);

		// 5. Re-enable projection
		await EnableProjection(name, ct);
		await WaitForProjectionStatus(name, "Running", TimeSpan.FromSeconds(30), ct);
		await Task.Delay(5000, ct);

		// 6. Flush results
		await FlushProjectionResults(name, ct);
		await Task.Delay(2000, ct);

		// 7. Verify
		var stream1Result = $"$projections-{name}-{stream1}-result";
		var stream2Result = $"$projections-{name}-{stream2}-result";
		await AssertResultStreamTail(stream1Result, """{"a":2,"deleted":1}""", ct);
		await AssertResultStreamTail(stream2Result, """{"a":2}""", ct);
	}

	#endregion
}
