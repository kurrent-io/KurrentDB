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

	// A link ($>) read from $all with resolveLinks:true whose target event has been scavenged
	// (or whose target stream was deleted) resolves to a ResolvedEvent with a null Event.
	static CoreResolvedEvent CreateFailedResolvedLink(string linkStreamId,
		long eventNumber,
		long logPosition,
		ReadEventResult resolveResult = ReadEventResult.StreamDeleted) {
		var link = CreateEventRecord(linkStreamId, eventNumber, logPosition, "$>", $"{eventNumber}@scavenged-stream");
		return CoreResolvedEvent.ForFailedResolvedLink(link, resolveResult, logPosition);
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
			MaxPartitionStateCacheSize = 1000,
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

		var hasCheckpoint = writes[0].Events.ToArray().Any(e => e.EventType == ProjectionEventTypes.ProjectionCheckpointV2);
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
			MaxPartitionStateCacheSize = 1000,
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
		await Assert.That(streamIds).Contains("$projections-state-test-stream-X-state");

		var eventsArray = lastWrite.Events.ToArray();
		var hasResult = eventsArray.Any(e => e.EventType == ProjectionEventTypes.ProjectionStateV2);
		await Assert.That(hasResult).IsTrue();

		var resultEvent = eventsArray.First(e => e.EventType == ProjectionEventTypes.ProjectionStateV2);
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
			MaxPartitionStateCacheSize = 1000,
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
		var hasCheckpoint = eventsArray.Any(e => e.EventType == ProjectionEventTypes.ProjectionCheckpointV2);
		await Assert.That(hasCheckpoint).IsTrue();

		var checkpointEvent = eventsArray.First(e => e.EventType == ProjectionEventTypes.ProjectionCheckpointV2);
		var json = Encoding.UTF8.GetString(checkpointEvent.Data);
		using var doc = JsonDocument.Parse(json);

		await Assert.That(doc.RootElement.TryGetProperty("commitPosition", out var commitProp)).IsTrue();
		await Assert.That(doc.RootElement.TryGetProperty("preparePosition", out var prepareProp)).IsTrue();
		await Assert.That(commitProp.GetInt64()).IsGreaterThan(0);
		await Assert.That(prepareProp.GetInt64()).IsGreaterThan(0);
	}

	[Test]
	public async Task skips_events_whose_type_is_not_declared_in_source_definition() {
		// Source declares only "Wanted" events. "Unwanted" must not reach the state handler —
		// otherwise Jint's Handle overwrites partition state with the event body when no handler matches.
		var events = new[] {
			CreateResolvedEvent("stream-F", 0, 100L, eventType: "Wanted"),
			CreateResolvedEvent("stream-F", 1, 200L, eventType: "Unwanted"),
			CreateResolvedEvent("stream-F", 2, 300L, eventType: "Wanted"),
			CreateResolvedEvent("stream-F", 3, 400L, eventType: "Unwanted"),
			CreateResolvedEvent("stream-F", 4, 500L, eventType: "Wanted"),
		};

		var config = new ProjectionEngineV2Config {
			ProjectionName = "filter-test",
			SourceDefinition = new QuerySourcesDefinition {
				AllStreams = true,
				AllEvents = false,
				Events = ["Wanted"],
				ByStreams = true
			},
			StateHandlerFactory = () => new CountingStateHandler(),
			MaxPartitionStateCacheSize = 1000,
			PartitionCount = 1,
			CheckpointAfterMs = 0,
			CheckpointHandledThreshold = 100,
			CheckpointUnhandledBytesThreshold = long.MaxValue
		};

		var (engine, publisher) = await RunEngine(events, config);

		await Assert.That(engine.IsFaulted).IsFalse();
		await Assert.That(engine.TotalEventsProcessed).IsEqualTo(3);

		var lastWrite = publisher.Messages.OfType<ClientMessage.WriteEvents>().LastOrDefault();
		await Assert.That(lastWrite).IsNotNull();

		var stateEvent = lastWrite!.Events.ToArray()
			.First(e => e.EventType == ProjectionEventTypes.ProjectionStateV2);
		var stateJson = Encoding.UTF8.GetString(stateEvent.Data);
		using var doc = JsonDocument.Parse(stateJson);
		await Assert.That(doc.RootElement.GetProperty("count").GetInt32()).IsEqualTo(3);
	}

	[Test]
	public async Task checkpoint_advances_past_trailing_filtered_events() {
		// Filtered events in the tail must still move the checkpoint forward so that
		// restart doesn't re-read them every time.
		var events = new[] {
			CreateResolvedEvent("stream-G", 0, 100L, eventType: "Wanted"),
			CreateResolvedEvent("stream-G", 1, 500L, eventType: "Unwanted"),
			CreateResolvedEvent("stream-G", 2, 900L, eventType: "Unwanted"),
		};

		var config = new ProjectionEngineV2Config {
			ProjectionName = "trailing-filter-test",
			SourceDefinition = new QuerySourcesDefinition {
				AllStreams = true,
				AllEvents = false,
				Events = ["Wanted"],
				ByStreams = true
			},
			StateHandlerFactory = () => new CountingStateHandler(),
			MaxPartitionStateCacheSize = 1000,
			PartitionCount = 1,
			CheckpointAfterMs = 0,
			CheckpointHandledThreshold = 100,
			CheckpointUnhandledBytesThreshold = long.MaxValue
		};

		var (engine, publisher) = await RunEngine(events, config);

		await Assert.That(engine.IsFaulted).IsFalse();
		await Assert.That(engine.TotalEventsProcessed).IsEqualTo(1);

		var lastWrite = publisher.Messages.OfType<ClientMessage.WriteEvents>().LastOrDefault();
		await Assert.That(lastWrite).IsNotNull();

		var checkpointEvent = lastWrite!.Events.ToArray()
			.First(e => e.EventType == ProjectionEventTypes.ProjectionCheckpointV2);
		using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(checkpointEvent.Data));
		await Assert.That(doc.RootElement.GetProperty("commitPosition").GetInt64()).IsEqualTo(900);
	}

	[Test]
	public async Task advances_checkpoint_past_scavenged_links_without_faulting() {
		// Reading filtered $all with resolveLinks:true can surface a link ($>) whose target event
		// has been scavenged (or whose target stream was deleted). The storage reader returns a
		// ResolvedEvent with a null Event in that case. The engine must:
		//   (a) not dereference the missing Event — previously that threw an NRE and faulted the
		//       whole read loop, stalling the projection at the dangling link; and
		//   (b) still advance the checkpoint past it — otherwise a long run of scavenged events
		//       never moves the checkpoint forward and gets re-read on every restart.
		// A scavenged link sits both between real events and as the trailing record so the final
		// checkpoint position must reflect the scavenged link, not the last real event.
		var events = new[] {
			CreateResolvedEvent("stream-H", 0, 100L),
			CreateFailedResolvedLink("$et-TestEvent", 0, 500L),
			CreateResolvedEvent("stream-H", 1, 700L),
			CreateFailedResolvedLink("$et-TestEvent", 1, 900L),
		};

		var config = new ProjectionEngineV2Config {
			ProjectionName = "scavenged-link-test",
			SourceDefinition = new QuerySourcesDefinition { AllStreams = true, AllEvents = true, ByStreams = true },
			StateHandlerFactory = () => new CountingStateHandler(),
			MaxPartitionStateCacheSize = 1000,
			PartitionCount = 1,
			CheckpointAfterMs = 0,
			CheckpointHandledThreshold = 100,
			CheckpointUnhandledBytesThreshold = long.MaxValue
		};

		var (engine, publisher) = await RunEngine(events, config);

		await Assert.That(engine.IsFaulted).IsFalse();
		// Only the two real events are dispatched; the scavenged links are skipped, not counted.
		await Assert.That(engine.TotalEventsProcessed).IsEqualTo(2);

		// The checkpoint must have advanced past the trailing scavenged link (900), not stopped
		// at the last dispatched event (700).
		var lastWrite = publisher.Messages.OfType<ClientMessage.WriteEvents>().LastOrDefault();
		await Assert.That(lastWrite).IsNotNull();
		var checkpointEvent = lastWrite!.Events.ToArray()
			.First(e => e.EventType == ProjectionEventTypes.ProjectionCheckpointV2);
		using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(checkpointEvent.Data));
		await Assert.That(doc.RootElement.GetProperty("commitPosition").GetInt64()).IsEqualTo(900);
	}
}
