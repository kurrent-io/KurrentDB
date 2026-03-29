// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
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

public class ProjectionEngineV2PipelineTests {
	#region Fakes

	sealed class FakeReadStrategy(CoreResolvedEvent[] events) : IReadStrategy {
		public async IAsyncEnumerable<ReadResponse> ReadFrom(TFPos checkpoint, [EnumeratorCancellation] CancellationToken ct) {
			foreach (var e in events) {
				ct.ThrowIfCancellationRequested();
				yield return new ReadResponse.EventReceived(e);
				await Task.Yield();
			}
		}
	}

	sealed class CapturingPublisher : IPublisher {
		public ConcurrentBag<Message> Messages { get; } = [];

		public void Publish(Message message) {
			Messages.Add(message);

			if (message is ClientMessage.WriteEvents writeEvents) {
				var numStreams = writeEvents.EventStreamIds.Length;
				var firstEventNumbers = new long[numStreams];
				var lastEventNumbers = new long[numStreams];

				var completed = new ClientMessage.WriteEventsCompleted(
					writeEvents.CorrelationId,
					firstEventNumbers,
					lastEventNumbers,
					preparePosition: 0,
					commitPosition: 0);

				writeEvents.Envelope.ReplyWith(completed);
			}

			if (message is ClientMessage.ReadStreamEventsBackward readBackward) {
				readBackward.Envelope.ReplyWith(
					new ClientMessage.ReadStreamEventsBackwardCompleted(
						readBackward.CorrelationId,
						readBackward.EventStreamId,
						readBackward.FromEventNumber,
						readBackward.MaxCount,
						ReadStreamResult.NoStream,
						[],
						streamMetadata: null,
						isCachePublic: false,
						error: string.Empty,
						nextEventNumber: -1,
						lastEventNumber: -1,
						isEndOfStream: true,
						tfLastCommitPosition: 0));
			}
		}
	}

	sealed class CountingStateHandler : IProjectionStateHandler {
		int _count;

		public void Load(string state) {
			if (string.IsNullOrEmpty(state) || state == "{}") {
				_count = 0;
				return;
			}

			try {
				using var doc = JsonDocument.Parse(state);
				_count = doc.RootElement.TryGetProperty("count", out var p) ? p.GetInt32() : 0;
			} catch { _count = 0; }
		}

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
			_count++;
			newState = $"{{\"count\":{_count}}}";
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

	static EventRecord CreateEventRecord(string streamId, long eventNumber, long logPosition, string eventType, string data) =>
		new(
			eventNumber: eventNumber,
			logPosition: logPosition,
			correlationId: Guid.NewGuid(),
			eventId: Guid.NewGuid(),
			transactionPosition: logPosition,
			transactionOffset: 0,
			eventStreamId: streamId,
			expectedVersion: eventNumber - 1,
			timeStamp: DateTime.UtcNow,
			flags: PrepareFlags.SingleWrite | PrepareFlags.TransactionBegin | PrepareFlags.TransactionEnd | PrepareFlags.IsJson,
			eventType: eventType,
			data: Encoding.UTF8.GetBytes(data),
			metadata: "{}"u8.ToArray());

	static CoreResolvedEvent CreateResolvedEvent(string streamId,
		long eventNumber,
		long logPosition,
		string eventType = "TestEvent",
		string data = "{}") {
		var record = CreateEventRecord(streamId, eventNumber, logPosition, eventType, data);
		return CoreResolvedEvent.ForUnresolvedEvent(record, logPosition);
	}

	async Task<(ProjectionEngineV2 Engine, CapturingPublisher Publisher)> RunEngine(
		CoreResolvedEvent[] events,
		ProjectionEngineV2Config config) {
		var publisher = new CapturingPublisher();
		var user = new ClaimsPrincipal(new ClaimsIdentity());
		var readStrategy = new FakeReadStrategy(events);
		var engine = new ProjectionEngineV2(config, readStrategy, new SystemClient(publisher), user);

		engine.Start(new TFPos(0, 0));

		var timeout = Task.Delay(TimeSpan.FromSeconds(10));
		while (!engine.IsFaulted) {
			if (publisher.Messages.Count > 0) {
				await Task.Delay(200);
				break;
			}

			if (timeout.IsCompleted) break;
			await Task.Delay(50);
		}

		await engine.DisposeAsync();

		return (engine, publisher);
	}

	[Test]
	public async Task processes_events_and_writes_checkpoint() {
		var events = new CoreResolvedEvent[10];
		for (int i = 0; i < 10; i++) {
			var stream = i % 2 == 0 ? "stream-A" : "stream-B";
			events[i] = CreateResolvedEvent(stream, i / 2, (i + 1) * 100L);
		}

		var stateHandler = new CountingStateHandler();
		var config = new ProjectionEngineV2Config {
			ProjectionName = "test-projection",
			SourceDefinition = stateHandler.GetSourceDefinition(),
			StateHandlerFactory = () => new CountingStateHandler(),
			PartitionCount = 1,
			CheckpointAfterMs = 0,
			CheckpointHandledThreshold = 5,
			CheckpointUnhandledBytesThreshold = long.MaxValue
		};

		var (engine, publisher) = await RunEngine(events, config);

		await Assert.That(engine.IsFaulted).IsFalse();

		var writes = publisher.Messages.OfType<ClientMessage.WriteEvents>().ToList();
		await Assert.That(writes.Count).IsGreaterThanOrEqualTo(1);

		var streamIds = Enumerable.Range(0, writes[0].EventStreamIds.Length)
			.Select(i => writes[0].EventStreamIds.Span[i]).ToList();
		await Assert.That(streamIds).Contains("$projections-test-projection-checkpoint");

		var hasCheckpoint = writes[0].Events.ToArray().Any(e => e.EventType == "$ProjectionCheckpoint");
		await Assert.That(hasCheckpoint).IsTrue();
	}

	[Test]
	public async Task checkpoint_contains_partition_state() {
		var events = new CoreResolvedEvent[10];
		for (int i = 0; i < 10; i++)
			events[i] = CreateResolvedEvent("stream-X", i, (i + 1) * 100L, data: $"{{\"value\":{i}}}");

		var stateHandler = new CountingStateHandler();
		var config = new ProjectionEngineV2Config {
			ProjectionName = "state-test",
			SourceDefinition = new QuerySourcesDefinition { AllStreams = true, AllEvents = true, ByStreams = true },
			StateHandlerFactory = () => new CountingStateHandler(),
			PartitionCount = 1,
			CheckpointAfterMs = 0,
			CheckpointHandledThreshold = 5,
			CheckpointUnhandledBytesThreshold = long.MaxValue
		};

		var (engine, publisher) = await RunEngine(events, config);

		await Assert.That(engine.IsFaulted).IsFalse();

		var lastWrite = publisher.Messages.OfType<ClientMessage.WriteEvents>().LastOrDefault();
		await Assert.That(lastWrite).IsNotNull();

		var streamIds = Enumerable.Range(0, lastWrite!.EventStreamIds.Length)
			.Select(i => lastWrite.EventStreamIds.Span[i]).ToList();
		await Assert.That(streamIds).Contains("$projections-state-test-stream-X-result");

		var eventsArray = lastWrite.Events.ToArray();
		var hasResult = eventsArray.Any(e => e.EventType == "Result");
		await Assert.That(hasResult).IsTrue();

		var resultEvent = eventsArray.First(e => e.EventType == "Result");
		var stateJson = Encoding.UTF8.GetString(resultEvent.Data);
		await Assert.That(stateJson).Contains("\"count\":");
	}

	[Test]
	public async Task checkpoint_position_reflects_last_processed_event() {
		var events = new CoreResolvedEvent[6];
		for (int i = 0; i < 6; i++)
			events[i] = CreateResolvedEvent("stream-Z", i, (i + 1) * 100L);

		var stateHandler = new CountingStateHandler();
		var config = new ProjectionEngineV2Config {
			ProjectionName = "pos-test",
			SourceDefinition = stateHandler.GetSourceDefinition(),
			StateHandlerFactory = () => new CountingStateHandler(),
			PartitionCount = 1,
			CheckpointAfterMs = 0,
			CheckpointHandledThreshold = 5,
			CheckpointUnhandledBytesThreshold = long.MaxValue
		};

		var (engine, publisher) = await RunEngine(events, config);

		await Assert.That(engine.IsFaulted).IsFalse();

		var firstWrite = publisher.Messages.OfType<ClientMessage.WriteEvents>().FirstOrDefault();
		await Assert.That(firstWrite).IsNotNull();

		var eventsArray = firstWrite!.Events.ToArray();
		var hasCheckpoint = eventsArray.Any(e => e.EventType == "$ProjectionCheckpoint");
		await Assert.That(hasCheckpoint).IsTrue();

		var checkpointEvent = eventsArray.First(e => e.EventType == "$ProjectionCheckpoint");
		var json = Encoding.UTF8.GetString(checkpointEvent.Data);
		using var doc = JsonDocument.Parse(json);

		await Assert.That(doc.RootElement.TryGetProperty("commitPosition", out var commitProp)).IsTrue();
		await Assert.That(doc.RootElement.TryGetProperty("preparePosition", out var prepareProp)).IsTrue();
		await Assert.That(commitProp.GetInt64()).IsGreaterThan(0);
		await Assert.That(prepareProp.GetInt64()).IsGreaterThan(0);
	}
}
