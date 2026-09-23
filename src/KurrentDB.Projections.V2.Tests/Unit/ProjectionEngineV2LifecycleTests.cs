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

	class NoOpStateHandler : IProjectionStateHandler {
		public void Load(string state) { }
		public void LoadShared(string state) { }
		public void Initialize() { }
		public void InitializeShared() { }

		public string GetStatePartition(CheckpointTag eventPosition, string category, ProjectionResolvedEvent data) =>
			data.EventStreamId;

		public virtual bool ProcessEvent(string partition,
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

	sealed class ThrowingStateHandler : NoOpStateHandler {
		public override bool ProcessEvent(string partition,
			CheckpointTag eventPosition,
			string category,
			ProjectionResolvedEvent @event,
			out string newState,
			out string newSharedState,
			out EmittedEventEnvelope[] emittedEvents) {
			throw new Exception("handler boom");
		}
	}

	// Processes the first event normally, throws on the second. Used to fault a
	// partition while a checkpoint marker can be in flight.
	sealed class ThrowOnSecondEventStateHandler : NoOpStateHandler {
		private int _seen;

		public override bool ProcessEvent(string partition,
			CheckpointTag eventPosition,
			string category,
			ProjectionResolvedEvent @event,
			out string newState,
			out string newSharedState,
			out EmittedEventEnvelope[] emittedEvents) {
			if (++_seen >= 2)
				throw new Exception("handler boom");
			return base.ProcessEvent(partition, eventPosition, category, @event, out newState, out newSharedState, out emittedEvents);
		}
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
	public async Task engine_faults_when_partition_processor_throws() {
		// DB-2159: a state-handler exception faults the partition processor's task, but
		// nothing observed partition tasks while the read loop ran, so a continuous
		// projection never faulted - it reported Running forever with no checkpoint, no
		// persistence, and nothing logged. The infinite read strategy reproduces the
		// continuous case; a finite stream would surface the fault via the end-of-read
		// drain and mask the bug.
		var publisher = new CapturingPublisher();
		var user = new ClaimsPrincipal(new ClaimsIdentity());
		var stateHandler = new ThrowingStateHandler();

		var config = new ProjectionEngineV2Config {
			ProjectionName = "faulting-test",
			SourceDefinition = stateHandler.GetSourceDefinition(),
			StateHandlerFactory = () => new ThrowingStateHandler(),
			MaxPartitionStateCacheSize = 1000,
			PartitionCount = 1,
			CheckpointAfterMs = 0,
			CheckpointHandledThreshold = 100,
			CheckpointUnhandledBytesThreshold = long.MaxValue
		};

		var engine = new ProjectionEngineV2(config, new InfiniteReadStrategy(), new SystemClient(publisher), user);
		engine.Start(new TFPos(0, 0));

		var timeout = Task.Delay(TimeSpan.FromSeconds(5));
		while (!engine.IsFaulted && !timeout.IsCompleted)
			await Task.Delay(50);

		// Assert before disposing: on a wedged engine DisposeAsync never returns (the
		// read loop's final checkpoint marker blocks forever on the dead partition's
		// full channel), so disposing first turns a failure into a hang.
		await Assert.That(engine.IsFaulted).IsTrue();
		// The fault reason names the projection, the handler, and the event position,
		// and carries the handler's error message.
		await Assert.That(engine.FaultException!.Message).Contains("failed to process an event");
		await Assert.That(engine.FaultException!.Message).Contains("handler boom");

		await engine.DisposeAsync();
	}

	[Test]
	public async Task engine_faults_when_processor_throws_with_checkpoint_in_flight() {
		// A checkpoint marker the dead partition never acks holds the coordinator's
		// checkpoint lock forever, so the fault path must cancel the read loop's
		// final marker injection rather than wait on the lock. Threshold 1 keeps
		// markers in flight around the fault; the handler processes one event and
		// throws on the second. The exact interleaving of marker injection and the
		// fault race is timing-dependent, but every interleaving must fault - a
		// wedge here reproduces DB-2159's narrowed variant.
		var publisher = new CapturingPublisher();
		var user = new ClaimsPrincipal(new ClaimsIdentity());
		var stateHandler = new ThrowOnSecondEventStateHandler();

		var config = new ProjectionEngineV2Config {
			ProjectionName = "faulting-checkpoint-test",
			SourceDefinition = stateHandler.GetSourceDefinition(),
			StateHandlerFactory = () => new ThrowOnSecondEventStateHandler(),
			MaxPartitionStateCacheSize = 1000,
			PartitionCount = 1,
			CheckpointAfterMs = 0,
			CheckpointHandledThreshold = 1,
			CheckpointUnhandledBytesThreshold = long.MaxValue
		};

		var engine = new ProjectionEngineV2(config, new InfiniteReadStrategy(), new SystemClient(publisher), user);
		engine.Start(new TFPos(0, 0));

		var timeout = Task.Delay(TimeSpan.FromSeconds(5));
		while (!engine.IsFaulted && !timeout.IsCompleted)
			await Task.Delay(50);

		// Assert before disposing (see engine_faults_when_partition_processor_throws).
		await Assert.That(engine.IsFaulted).IsTrue();
		await Assert.That(engine.FaultException!.Message).Contains("handler boom");

		await engine.DisposeAsync();
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
