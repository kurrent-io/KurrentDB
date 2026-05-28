// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using KurrentDB.Core;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Services.Transport.Enumerators;
using KurrentDB.Core.TransactionLog.LogRecords;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using KurrentDB.Projections.Core.Services.Processing.Emitting.EmittedEvents;
using KurrentDB.Projections.Core.Services.Processing.V2;
using CoreResolvedEvent = KurrentDB.Core.Data.ResolvedEvent;
using ProjectionResolvedEvent = KurrentDB.Projections.Core.Services.Processing.ResolvedEvent;

namespace KurrentDB.Projections.V2.Tests.Unit;

// todo: whats with all the delays in this test project
public class ProjectionEngineV2LifecycleTests {
	#region Helpers

	sealed class InfiniteReadStrategy : IReadStrategy {
		public async IAsyncEnumerable<ReadResponse> ReadFrom(
			TFPos checkpoint,
			[EnumeratorCancellation] CancellationToken ct) {
			long pos = 100;
			while (!ct.IsCancellationRequested) {
				var record = new EventRecord(
					eventNumber: pos / 100, logPosition: pos,
					correlationId: Guid.NewGuid(), eventId: Guid.NewGuid(),
					transactionPosition: pos, transactionOffset: 0,
					eventStreamId: "stream-inf", expectedVersion: pos / 100 - 1,
					timeStamp: DateTime.UtcNow,
					flags: PrepareFlags.SingleWrite | PrepareFlags.TransactionBegin | PrepareFlags.TransactionEnd | PrepareFlags.IsJson,
					eventType: "TestEvent",
					data: "{}"u8.ToArray(),
					metadata: "{}"u8.ToArray());
				yield return new ReadResponse.EventReceived(CoreResolvedEvent.ForUnresolvedEvent(record, pos));
				pos += 100;
				await Task.Delay(10, ct);
			}
		}

	}

	sealed class FakeReadStrategy(CoreResolvedEvent[] events) : IReadStrategy {
		public async IAsyncEnumerable<ReadResponse> ReadFrom(TFPos checkpoint, [EnumeratorCancellation] CancellationToken ct) {
			foreach (var e in events) {
				ct.ThrowIfCancellationRequested();
				yield return new ReadResponse.EventReceived(e);
				await Task.Yield();
			}
		}

	}

	sealed class EmptyReadStrategy : IReadStrategy {
		public async IAsyncEnumerable<ReadResponse> ReadFrom(TFPos checkpoint, [EnumeratorCancellation] CancellationToken ct) {
			await Task.CompletedTask;
			yield break;
		}

	}

	sealed class CapturingPublisher : IPublisher {
		public ConcurrentBag<Message> Messages { get; } = [];

		public void Publish(Message message) {
			Messages.Add(message);
			if (message is ClientMessage.WriteEvents w) {
				var first = new long[w.EventStreamIds.Length];
				var last = new long[w.EventStreamIds.Length];
				w.Envelope.ReplyWith(new ClientMessage.WriteEventsCompleted(
					w.CorrelationId, first, last, preparePosition: 0, commitPosition: 0));
			}

			if (message is ClientMessage.ReadStreamEventsBackward rb) {
				rb.Envelope.ReplyWith(
					new ClientMessage.ReadStreamEventsBackwardCompleted(
						rb.CorrelationId, rb.EventStreamId, rb.FromEventNumber, rb.MaxCount,
						ReadStreamResult.NoStream, [], streamMetadata: null, isCachePublic: false,
						error: string.Empty, nextEventNumber: -1, lastEventNumber: -1,
						isEndOfStream: true, tfLastCommitPosition: 0));
			}
		}
	}

	sealed class NoOpStateHandler : IProjectionStateHandler {
		public void Load(string state) { }
		public void LoadShared(string state) { }
		public void Initialize() { }
		public void InitializeShared() { }

		public string GetStatePartition(CheckpointTag eventPosition, string category, ProjectionResolvedEvent data) =>
			data.EventStreamId;

		public bool ProcessEvent(string partition,
			CheckpointTag eventPosition,
			string category,
			ProjectionResolvedEvent @event,
			out string newState,
			out string newSharedState,
			out EmittedEventEnvelope[] emittedEvents) {
			newState = "{}";
			newSharedState = null!;
			emittedEvents = null!;
			return true;
		}

		public bool ProcessPartitionCreated(string partition,
			CheckpointTag createPosition,
			ProjectionResolvedEvent @event,
			out EmittedEventEnvelope[] emittedEvents) {
			emittedEvents = null!;
			return false;
		}

		public bool ProcessPartitionDeleted(string partition, CheckpointTag deletePosition, out string newState) {
			newState = null!;
			return false;
		}

		public string TransformStateToResult() => null!;

		public IQuerySources GetSourceDefinition() => new QuerySourcesDefinition {
			AllStreams = true,
			AllEvents = true
		};

		public void Dispose() { }
	}

	#endregion

	[Test]
	public async Task engine_stops_gracefully_on_cancellation() {
		var publisher = new CapturingPublisher();
		var user = new ClaimsPrincipal(new ClaimsIdentity());
		var stateHandler = new NoOpStateHandler();

		var config = new ProjectionEngineV2Config {
			ProjectionName = "cancel-test",
			SourceDefinition = stateHandler.GetSourceDefinition(),
			StateHandlerFactory = () => new NoOpStateHandler(),
			MaxPartitionStateCacheSize = 1000,
			PartitionCount = 1,
			CheckpointAfterMs = 0,
			CheckpointHandledThreshold = 100,
			CheckpointUnhandledBytesThreshold = long.MaxValue
		};

		var engine = new ProjectionEngineV2(config, new InfiniteReadStrategy(), new SystemClient(publisher), user);

		engine.Start(new TFPos(0, 0));
		await engine.DisposeAsync();

		await Assert.That(engine.IsFaulted).IsFalse();
	}

	[Test]
	public async Task engine_handles_empty_read_stream() {
		var publisher = new CapturingPublisher();
		var user = new ClaimsPrincipal(new ClaimsIdentity());
		var stateHandler = new NoOpStateHandler();

		var config = new ProjectionEngineV2Config {
			ProjectionName = "empty-test",
			SourceDefinition = stateHandler.GetSourceDefinition(),
			StateHandlerFactory = () => new NoOpStateHandler(),
			MaxPartitionStateCacheSize = 1000,
			PartitionCount = 1,
			CheckpointAfterMs = 0,
			CheckpointHandledThreshold = 5,
			CheckpointUnhandledBytesThreshold = long.MaxValue
		};

		var engine = new ProjectionEngineV2(config, new EmptyReadStrategy(), new SystemClient(publisher), user);

		engine.Start(new TFPos(0, 0));
		await engine.DisposeAsync();

		await Assert.That(engine.IsFaulted).IsFalse();
	}

	[Test]
	public async Task engine_with_multiple_partitions_processes_events() {
		var events = new CoreResolvedEvent[20];
		for (int i = 0; i < 20; i++) {
			var stream = $"stream-{i % 4}";
			var pos = (i + 1) * 100L;
			var record = new EventRecord(
				eventNumber: i / 4, logPosition: pos,
				correlationId: Guid.NewGuid(), eventId: Guid.NewGuid(),
				transactionPosition: pos, transactionOffset: 0,
				eventStreamId: stream, expectedVersion: i / 4 - 1,
				timeStamp: DateTime.UtcNow,
				flags: PrepareFlags.SingleWrite | PrepareFlags.TransactionBegin | PrepareFlags.TransactionEnd | PrepareFlags.IsJson,
				eventType: "TestEvent",
				data: "{}"u8.ToArray(),
				metadata: "{}"u8.ToArray());
			events[i] = CoreResolvedEvent.ForUnresolvedEvent(record, pos);
		}

		var publisher = new CapturingPublisher();
		var user = new ClaimsPrincipal(new ClaimsIdentity());
		var stateHandler = new NoOpStateHandler();

		var config = new ProjectionEngineV2Config {
			ProjectionName = "multi-partition-test",
			SourceDefinition = new QuerySourcesDefinition { AllStreams = true, AllEvents = true, ByStreams = true },
			StateHandlerFactory = () => new NoOpStateHandler(),
			MaxPartitionStateCacheSize = 1000,
			PartitionCount = 4,
			CheckpointAfterMs = 0,
			CheckpointHandledThreshold =
				100, // High threshold so only the final checkpoint fires, avoiding a race in CheckpointCoordinator when multiple markers are injected and partitions process at different speeds
			CheckpointUnhandledBytesThreshold = long.MaxValue
		};

		var readStrategy = new FakeReadStrategy(events);
		var engine = new ProjectionEngineV2(config, readStrategy, new SystemClient(publisher), user);
		engine.Start(new TFPos(0, 0));

		var timeout = Task.Delay(TimeSpan.FromSeconds(10));
		while (!engine.IsFaulted) {
			if (publisher.Messages.Count > 0) {
				await Task.Delay(300);
				break;
			}

			if (timeout.IsCompleted) break;
			await Task.Delay(50);
		}

		await engine.DisposeAsync();

		await Assert.That(engine.IsFaulted).IsFalse();

		var writes = publisher.Messages.OfType<ClientMessage.WriteEvents>().ToList();
		await Assert.That(writes.Count).IsGreaterThanOrEqualTo(1);
	}
}
