// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Google.Protobuf;
using KurrentDB.Core;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using KurrentDB.Projections.Core.Metrics;
using KurrentDB.Projections.Core.Services;
using KurrentDB.Projections.Core.Services.Interpreted;
using KurrentDB.Projections.Core.Services.Processing.V2;
using KurrentDB.Projections.Core.Services.Processing;
using KurrentDB.Projections.Core.Services.Processing.V2.ReadStrategies;
using KurrentDB.Projections.V2.Tests.Fixtures;
using KurrentDB.Protocol.V2.Streams;
using SchemaFormat = KurrentDB.Protocol.V2.Streams.SchemaFormat;
using SchemaInfo = KurrentDB.Protocol.V2.Streams.SchemaInfo;

namespace KurrentDB.Projections.V2.Tests.Integration;

/// <summary>
/// Integration tests that verify partition-state cache eviction under high-cardinality load,
/// and that evicted partition state is recoverable from the persisted -state stream.
/// </summary>
public class PartitionStateCacheEvictionTests {
	[ClassDataSource<ProjectionsNodeFixture>(Shared = SharedType.PerTestSession)]
	public required ProjectionsNodeFixture Fixture { get; init; }

	// Simple fromAll().foreachStream() projection: counts events per stream partition.
	const string CountingProjectionQuery = """
		fromAll().foreachStream().when({
			$init: function() { return { count: 0 }; },
			Counted: function(s, e) { s.count++; return s; },
		});
		""";

	static readonly ClaimsPrincipal AdminUser = new(new ClaimsIdentity([
		new Claim(ClaimTypes.Name, "admin"),
		new Claim(ClaimTypes.Role, "$admins")
	], "test"));

	static AppendRequest SingleEvent(string stream, string eventType = "Counted") {
		var request = new AppendRequest {
			Stream = stream,
			ExpectedRevision = -2 // Any
		};
		request.Records.Add(new AppendRecord {
			RecordId = Guid.NewGuid().ToString(),
			Data = ByteString.CopyFromUtf8("{}"),
			Schema = new SchemaInfo { Name = eventType, Format = SchemaFormat.Json }
		});
		return request;
	}

	/// <summary>
	/// Publishes writes to the real bus but also captures all messages so tests can inspect them.
	/// </summary>
	sealed class ForwardingCapturingPublisher(IPublisher inner) : IPublisher {
		public ConcurrentBag<Message> Messages { get; } = [];

		public void Publish(Message message) {
			Messages.Add(message);
			// Forward everything — writes go to real storage, reads work normally.
			inner.Publish(message);
		}
	}

	/// <summary>
	/// Intercepts write messages (replies success without persisting), forwards reads.
	/// Used by Test 1 where we don't need real state persistence.
	/// </summary>
	sealed class CapturingPublisher(IPublisher inner) : IPublisher {
		public ConcurrentBag<Message> Messages { get; } = [];

		public void Publish(Message message) {
			Messages.Add(message);
			if (message is ClientMessage.WriteEvents w) {
				var first = new long[w.EventStreamIds.Length];
				var last = new long[w.EventStreamIds.Length];
				w.Envelope.ReplyWith(new ClientMessage.WriteEventsCompleted(
					w.CorrelationId, first, last, preparePosition: 0, commitPosition: 0));
			} else {
				inner.Publish(message);
			}
		}
	}

	IProjectionStateHandler CreateCountingHandler(string projectionName) {
		var trackers = ProjectionTrackers.NoOp;
		return new JintProjectionStateHandler(
			CountingProjectionQuery,
			enableContentTypeValidation: false,
			compilationTimeout: TimeSpan.FromSeconds(5),
			executionTimeout: TimeSpan.FromSeconds(5),
			new(trackers.GetExecutionTrackerForProjection(projectionName)),
			new(trackers.GetSerializationTrackerForProjection(projectionName)));
	}

	async Task<ProjectionEngineV2> StartEngine(
		string projectionName,
		IPublisher publisher,
		int maxCacheSize,
		TFPos startFrom,
		int checkpointHandledThreshold = 1) {
		using var sourceHandler = CreateCountingHandler(projectionName);
		var sourceDefinition = sourceHandler.GetSourceDefinition();

		var readStrategy = ReadStrategyFactory.Create(sourceDefinition, Fixture.MainQueue, AdminUser);

		var config = new ProjectionEngineV2Config {
			ProjectionName = projectionName,
			SourceDefinition = sourceDefinition,
			StateHandlerFactory = () => CreateCountingHandler(projectionName),
			MaxPartitionStateCacheSize = maxCacheSize,
			CheckpointAfterMs = 0,
			CheckpointHandledThreshold = checkpointHandledThreshold,
			CheckpointUnhandledBytesThreshold = long.MaxValue,
			EmitEnabled = false
		};

		var engine = new ProjectionEngineV2(config, readStrategy, new SystemClient(publisher), AdminUser);
		engine.Start(startFrom);
		return engine;
	}

	static async Task WaitForCheckpoint(CapturingPublisher publisher, ProjectionEngineV2 engine, TimeSpan timeout) {
		var deadline = Task.Delay(timeout);
		while (!engine.IsFaulted) {
			var writeCount = publisher.Messages
				.OfType<ClientMessage.WriteEvents>()
				.Count();
			if (writeCount > 0) {
				await Task.Delay(500);
				break;
			}
			if (deadline.IsCompleted) break;
			await Task.Delay(50);
		}
	}

	static async Task WaitForEventsProcessed(ProjectionEngineV2 engine, long minEvents, TimeSpan timeout) {
		var deadline = Task.Delay(timeout);
		while (!engine.IsFaulted) {
			if (engine.TotalEventsProcessed >= minEvents) break;
			if (deadline.IsCompleted) break;
			await Task.Delay(50);
		}
	}

	/// <summary>
	/// Proves that feeding many distinct partitions into an engine with a small MaxPartitionStateCacheSize
	/// causes the shared cache eviction counter to increment. SIEVE eviction is asynchronous, so the
	/// test inserts far more entries than capacity and polls with a deadline.
	/// </summary>
	[Test]
	[Timeout(60_000)]
	public async Task eviction_counter_reflects_high_cardinality_load(CancellationToken ct) {
		const int cacheSize = 4;
		const int partitionCount = 40; // 10x capacity — gives SIEVE plenty to evict

		var testId = Guid.NewGuid().ToString("N")[..8];
		var projectionName = $"cache-eviction-{testId}";

		// 1. Write two events to each of 40 distinct streams (80 events total).
		//    Writing multiple events per partition gives the SIEVE background task time to run
		//    between partition-processor Set calls.
		for (var i = 0; i < partitionCount; i++) {
			await Fixture.StreamsClient.AppendAsync(SingleEvent($"p{i}-{testId}"), ct);
			await Fixture.StreamsClient.AppendAsync(SingleEvent($"p{i}-{testId}"), ct);
		}

		// Wait for the index to catch up so fromAll() can see all events.
		await Task.Delay(2000, ct);

		// 2. Start the engine with a tiny cache (capacity=4).
		var capturingPublisher = new CapturingPublisher(Fixture.MainQueue);
		var engine = await StartEngine(
			projectionName,
			capturingPublisher,
			maxCacheSize: cacheSize,
			startFrom: new TFPos(0, 0),
			checkpointHandledThreshold: partitionCount * 2);

		// 3. Wait until the engine has processed all 80 events then a checkpoint fires.
		await WaitForEventsProcessed(engine, partitionCount * 2, TimeSpan.FromSeconds(20));
		await WaitForCheckpoint(capturingPublisher, engine, TimeSpan.FromSeconds(10));

		// 4. Poll for SIEVE to fire (at least) partitionCount - cacheSize evictions.
		//    SIEVE runs on a background thread and may lag behind writes.
		const long expectedMinEvictions = partitionCount - cacheSize; // 36
		var evictionDeadline = Task.Delay(TimeSpan.FromSeconds(10), ct);
		while (engine.GetCacheMetrics().Evictions < expectedMinEvictions && !evictionDeadline.IsCompleted)
			await Task.Delay(20, CancellationToken.None);

		await engine.DisposeAsync();

		await Assert.That(engine.IsFaulted).IsFalse();

		var metrics = engine.GetCacheMetrics();

		// 5. Eviction counter must match the expected lower bound — proves the cache bounded
		//    its growth to (approximately) cacheSize. We can't assert Size <= cacheSize because
		//    PartitionStateCache.Count is an application-level counter that decrements only in
		//    the async Eviction callback; SIEVE's internal currentSize is correctly bounded but
		//    is not directly observable.
		await Assert.That(metrics.Evictions)
			.IsGreaterThanOrEqualTo(expectedMinEvictions)
			.Because($"Expected at least {expectedMinEvictions} evictions with {partitionCount} partitions and cache size {cacheSize}, got {metrics.Evictions}");
	}

	/// <summary>
	/// Proves that state for an evicted partition is recoverable from the persisted -state stream.
	/// The first engine run uses real storage writes (ForwardingCapturingPublisher). After it is
	/// disposed, a second engine is started fresh — its in-memory cache is empty — and a second
	/// event is appended to partition "p1". The second engine must load the persisted state from
	/// the -state stream before processing the new event, yielding count=2.
	/// </summary>
	[Test]
	[Timeout(60_000)]
	public async Task evicted_partition_state_is_recovered_from_stream(CancellationToken ct) {
		const int cacheSize = 4;
		const int firstBatchCount = 10;

		var testId = Guid.NewGuid().ToString("N")[..8];
		var projectionName = $"cache-recovery-{testId}";
		var p1Stream = $"p1-{testId}";

		// 1. Write one event to each of 10 distinct streams (p1..p10).
		for (var i = 1; i <= firstBatchCount; i++) {
			await Fixture.StreamsClient.AppendAsync(SingleEvent($"p{i}-{testId}"), ct);
		}

		// Wait for index.
		await Task.Delay(2000, ct);

		// 2. Run the first engine with real writes so states are persisted.
		var realPublisher = new ForwardingCapturingPublisher(Fixture.MainQueue);
		var engine1 = await StartEngine(
			projectionName,
			realPublisher,
			maxCacheSize: cacheSize,
			startFrom: new TFPos(0, 0),
			checkpointHandledThreshold: firstBatchCount);

		// Wait until all 10 events are processed and a checkpoint has been written.
		await WaitForEventsProcessed(engine1, firstBatchCount, TimeSpan.FromSeconds(20));

		// Wait for at least one checkpoint write to appear.
		var checkpointDeadline = Task.Delay(TimeSpan.FromSeconds(15));
		while (!engine1.IsFaulted) {
			var checkpointWrites = realPublisher.Messages
				.OfType<ClientMessage.WriteEvents>()
				.Any(w => {
					var evts = w.Events.ToArray();
					return evts.Any(e => e.EventType == ProjectionEventTypes.ProjectionCheckpointV2);
				});
			if (checkpointWrites) {
				// Allow extra time for state writes to complete.
				await Task.Delay(2000, ct);
				break;
			}
			if (checkpointDeadline.IsCompleted) break;
			await Task.Delay(100, ct);
		}

		// Record the log position that engine1 reached.
		var checkpointWrites1 = realPublisher.Messages
			.OfType<ClientMessage.WriteEvents>()
			.Where(w => w.Events.ToArray().Any(e => e.EventType == ProjectionEventTypes.ProjectionCheckpointV2))
			.ToList();

		await engine1.DisposeAsync();

		await Assert.That(engine1.IsFaulted).IsFalse()
			.Because($"Engine 1 faulted: {engine1.FaultException}");

		await Assert.That(checkpointWrites1.Count)
			.IsGreaterThanOrEqualTo(1)
			.Because("Engine 1 should have written at least one checkpoint");

		// 3. Verify that state for p1 was written to the -state stream.
		var p1StateStream = ProjectionNamesBuilder.MakeStateStreamName(projectionName, p1Stream);
		var p1PersistedState = await new SystemClient(Fixture.MainQueue).Reading
			.ReadStreamLastEvent(p1StateStream, ct);

		await Assert.That(p1PersistedState.HasValue)
			.IsTrue()
			.Because($"State stream '{p1StateStream}' should exist after the first engine run");

		var p1StateJson = Encoding.UTF8.GetString(p1PersistedState!.Value.Event.Data.Span);
		using var p1Doc1 = JsonDocument.Parse(p1StateJson);
		await Assert.That(p1Doc1.RootElement.GetProperty("count").GetInt32())
			.IsEqualTo(1)
			.Because("p1 should have count=1 after the first event");

		// 4. Write a second event to p1 and wait for index.
		await Fixture.StreamsClient.AppendAsync(SingleEvent(p1Stream), ct);
		await Task.Delay(2000, ct);

		// 5. Parse the last checkpoint position engine1 wrote — engine2 must start from that
		//    checkpoint (matching the production CoreProjectionV2 restart flow). Starting from
		//    TFPos(0,0) while also loading the -state snapshot would replay pre-snapshot events
		//    on top of a later state and yield incorrect counts.
		var lastCheckpointWrite = checkpointWrites1.Last();
		var lastCheckpointEvent = lastCheckpointWrite.Events.ToArray()
			.First(e => e.EventType == ProjectionEventTypes.ProjectionCheckpointV2);
		using var checkpointDoc = JsonDocument.Parse(lastCheckpointEvent.Data.ToArray());
		var checkpointPos = new TFPos(
			commitPosition: checkpointDoc.RootElement.GetProperty("commitPosition").GetInt64(),
			preparePosition: checkpointDoc.RootElement.GetProperty("preparePosition").GetInt64());

		// 6. Engine2 starts from engine1's checkpoint. It only sees events after that point —
		//    specifically, the second p1 event written in step 4. For p1:
		//      - cache miss (new engine, empty _stateCache)
		//      - loadPersistedState reads the -state stream → loads count=1
		//      - processes the new Counted event → count=2
		//    That exercises the stream-fallback recovery path exactly as a real restart would.
		var realPublisher2 = new ForwardingCapturingPublisher(Fixture.MainQueue);
		var engine2 = await StartEngine(
			projectionName,
			realPublisher2,
			maxCacheSize: cacheSize,
			startFrom: checkpointPos,
			checkpointHandledThreshold: 1);

		// Wait for the single post-checkpoint event (the second p1 event) to be processed.
		await WaitForEventsProcessed(engine2, 1, TimeSpan.FromSeconds(25));

		// Wait for a checkpoint write.
		var checkpointDeadline2 = Task.Delay(TimeSpan.FromSeconds(15));
		while (!engine2.IsFaulted) {
			var hasCheckpoint = realPublisher2.Messages
				.OfType<ClientMessage.WriteEvents>()
				.Any(w => w.Events.ToArray()
					.Any(e => e.EventType == ProjectionEventTypes.ProjectionCheckpointV2));
			if (hasCheckpoint) {
				await Task.Delay(1000, ct);
				break;
			}
			if (checkpointDeadline2.IsCompleted) break;
			await Task.Delay(100, ct);
		}

		await engine2.DisposeAsync();

		await Assert.That(engine2.IsFaulted).IsFalse()
			.Because($"Engine 2 faulted: {engine2.FaultException}");

		// 6. Check the persisted state for p1 after engine2 ran.
		//    Engine2's cache was empty at start (fresh instance).  p1 gets loaded from
		//    the -state stream (count=1) and then the new event increments it to count=2.
		var p1PersistedState2 = await new SystemClient(Fixture.MainQueue).Reading
			.ReadStreamLastEvent(p1StateStream, ct);

		await Assert.That(p1PersistedState2.HasValue)
			.IsTrue()
			.Because($"State stream '{p1StateStream}' should still exist after the second engine run");

		var p1StateJson2 = Encoding.UTF8.GetString(p1PersistedState2!.Value.Event.Data.Span);
		using var p1Doc2 = JsonDocument.Parse(p1StateJson2);
		await Assert.That(p1Doc2.RootElement.GetProperty("count").GetInt32())
			.IsEqualTo(2)
			.Because("p1 should have count=2 after two events — proves state was loaded from the -state stream by the second engine");
	}
}
