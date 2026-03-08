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
/// Tests that V2 projections handle $created notifications when a stream partition is first encountered.
/// Mirrors the V1 tests in KurrentDB.Projections.Core.Tests/ClientAPI/when_handling_created/.
/// </summary>
[NotInParallel]
public class CreatedStreamProjectionTests {
	[ClassDataSource<ProjectionsNodeFixture>(Shared = SharedType.PerTestSession)]
	public required ProjectionsNodeFixture Fixture { get; init; }

	#region Helpers

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

	async Task<string> CreateProjection(string source, int engineVersion, CancellationToken ct) {
		var testId = Guid.NewGuid().ToString("N")[..8];
		var name = $"cre-v{engineVersion}-{testId}";
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

	#region Tests — V2 Engine: fromAll().foreachStream() with $created

	/// <summary>
	/// Events written, then projection created with $any + $created handlers.
	/// Mirrors: with_from_all_any_foreach_projection/when_running_and_events_are_posted
	/// Uses fromCategory() instead of fromAll() because V2 runs on a shared server
	/// (V1 test used an isolated MiniNode with no other data).
	/// Each stream gets: $created (+1), type1 via $any (+1), type2 via $any (+1) = 3
	/// </summary>
	[Test]
	[Timeout(180_000)]
	public async Task v2_created_with_any_handler(CancellationToken ct) {
		var category = $"creanya{Guid.NewGuid():N}";
		var stream1 = $"{category}-1";
		var stream2 = $"{category}-2";

		// 1. Write events
		await AppendEvent(stream1, "type1", ct);
		await AppendEvent(stream1, "type2", ct);
		await AppendEvent(stream2, "type1", ct);
		await AppendEvent(stream2, "type2", ct);
		await Task.Delay(2000, ct);

		// 2. Create projection with $any + $created scoped to category
		var source = $$"""
			fromCategory('{{category}}').foreachStream().when({
				$init: function() { return { a: 0 } },
				$any: function(s, e) { s.a++; return s; },
				$created: function(s, e) { s.a++; return s; },
			}).outputState();
			""";
		var name = await CreateProjection(source, engineVersion: 2, ct);
		await WaitForProjectionStatus(name, "Running", TimeSpan.FromSeconds(30), ct);
		await WaitForEventsProcessed(name, 4, TimeSpan.FromSeconds(30), ct);

		// 3. Flush results
		await FlushProjectionResults(name, ct);
		await Task.Delay(2000, ct);

		// 4. Verify: each stream should have a=3 ($created + 2 events via $any)
		var stream1Result = $"$projections-{name}-{stream1}-result";
		var stream2Result = $"$projections-{name}-{stream2}-result";
		await AssertResultStreamTail(stream1Result, """{"a":3}""", ct);
		await AssertResultStreamTail(stream2Result, """{"a":3}""", ct);
	}

	/// <summary>
	/// Events written, then projection created with typed handlers + $created.
	/// Mirrors: with_from_all_foreach_projection/when_running_and_events_are_indexed
	/// Each stream gets: $created (+1), type1 (+1), type2 (+1) = 3
	/// </summary>
	[Test]
	[Timeout(180_000)]
	public async Task v2_created_with_typed_handlers(CancellationToken ct) {
		var stream1 = $"cre2b-{Guid.NewGuid():N}-1";
		var stream2 = $"cre2b-{Guid.NewGuid():N}-2";

		// 1. Write events
		await AppendEvent(stream1, "type1", ct);
		await AppendEvent(stream1, "type2", ct);
		await AppendEvent(stream2, "type1", ct);
		await AppendEvent(stream2, "type2", ct);
		await Task.Delay(2000, ct);

		// 2. Create projection with type1/type2 + $created
		const string source = """
			fromAll().foreachStream().when({
				$init: function() { return { a: 0 } },
				type1: function(s, e) { s.a++; return s; },
				type2: function(s, e) { s.a++; return s; },
				$created: function(s, e) { s.a++; return s; },
			}).outputState();
			""";
		var name = await CreateProjection(source, engineVersion: 2, ct);
		await WaitForProjectionStatus(name, "Running", TimeSpan.FromSeconds(30), ct);
		await WaitForEventsProcessed(name, 4, TimeSpan.FromSeconds(30), ct);

		// 3. Flush results
		await FlushProjectionResults(name, ct);
		await Task.Delay(2000, ct);

		// 4. Verify: each stream should have a=3 ($created + type1 + type2)
		var stream1Result = $"$projections-{name}-{stream1}-result";
		var stream2Result = $"$projections-{name}-{stream2}-result";
		await AssertResultStreamTail(stream1Result, """{"a":3}""", ct);
		await AssertResultStreamTail(stream2Result, """{"a":3}""", ct);
	}

	#endregion

	#region Tests — V2 Engine: fromCategory().foreachStream() with $created

	/// <summary>
	/// Events written, then category projection created with typed handlers + $created.
	/// Mirrors: with_from_category_foreach_projection/when_running_and_events_are_indexed
	/// Each stream gets: $created (+1), type1 (+1), type2 (+1) = 3
	/// </summary>
	[Test]
	[Timeout(180_000)]
	public async Task v2_category_created_with_typed_handlers(CancellationToken ct) {
		var category = $"crecatc{Guid.NewGuid():N}";
		var stream1 = $"{category}-1";
		var stream2 = $"{category}-2";

		// 1. Write events
		await AppendEvent(stream1, "type1", ct);
		await AppendEvent(stream1, "type2", ct);
		await AppendEvent(stream2, "type1", ct);
		await AppendEvent(stream2, "type2", ct);
		await Task.Delay(2000, ct);

		// 2. Create projection with type1/type2 + $created for the category
		var source = $$"""
			fromCategory('{{category}}').foreachStream().when({
				$init: function() { return { a: 0 } },
				type1: function(s, e) { s.a++; return s; },
				type2: function(s, e) { s.a++; return s; },
				$created: function(s, e) { s.a++; return s; },
			}).outputState();
			""";
		var name = await CreateProjection(source, engineVersion: 2, ct);
		await WaitForProjectionStatus(name, "Running", TimeSpan.FromSeconds(30), ct);
		await WaitForEventsProcessed(name, 4, TimeSpan.FromSeconds(30), ct);

		// 3. Flush results
		await FlushProjectionResults(name, ct);
		await Task.Delay(2000, ct);

		// 4. Verify: each stream should have a=3 ($created + type1 + type2)
		var stream1Result = $"$projections-{name}-{stream1}-result";
		var stream2Result = $"$projections-{name}-{stream2}-result";
		await AssertResultStreamTail(stream1Result, """{"a":3}""", ct);
		await AssertResultStreamTail(stream2Result, """{"a":3}""", ct);
	}

	#endregion
}
