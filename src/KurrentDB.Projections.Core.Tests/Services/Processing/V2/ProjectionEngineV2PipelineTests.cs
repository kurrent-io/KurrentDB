// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
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
using NUnit.Framework;
using CoreResolvedEvent = KurrentDB.Core.Data.ResolvedEvent;
using ProjectionResolvedEvent = KurrentDB.Projections.Core.Services.Processing.ResolvedEvent;

namespace KurrentDB.Projections.Core.Tests.Services.Processing.V2;

[TestFixture]
public class ProjectionEngineV2PipelineTests {

	#region Fakes

	/// <summary>
	/// A fake read strategy that yields a fixed sequence of events then completes.
	/// </summary>
	private sealed class FakeReadStrategy : IReadStrategy {
		private readonly CoreResolvedEvent[] _events;

		public FakeReadStrategy(CoreResolvedEvent[] events) {
			_events = events;
		}

		public async IAsyncEnumerable<ReadResponse> ReadFrom(
			TFPos checkpoint, [EnumeratorCancellation] CancellationToken ct) {
			foreach (var e in _events) {
				ct.ThrowIfCancellationRequested();
				yield return new ReadResponse.EventReceived(e);
				// Small yield to allow pipeline tasks to make progress
				await Task.Yield();
			}
		}

		public ValueTask DisposeAsync() => ValueTask.CompletedTask;
	}

	/// <summary>
	/// A fake publisher that captures all published messages and auto-replies
	/// to WriteEvents with a success response.
	/// </summary>
	private sealed class CapturingPublisher : IPublisher {
		public ConcurrentBag<Message> Messages { get; } = new();

		public void Publish(Message message) {
			Messages.Add(message);

			// Auto-reply with success for WriteEvents so the coordinator unblocks
			if (message is ClientMessage.WriteEvents writeEvents) {
				var numStreams = writeEvents.EventStreamIds.Length;
				var firstEventNumbers = new long[numStreams];
				var lastEventNumbers = new long[numStreams];
				for (int i = 0; i < numStreams; i++) {
					firstEventNumbers[i] = 0;
					lastEventNumbers[i] = 0;
				}

				var completed = new ClientMessage.WriteEventsCompleted(
					writeEvents.CorrelationId,
					firstEventNumbers,
					lastEventNumbers,
					preparePosition: 0,
					commitPosition: 0);

				writeEvents.Envelope.ReplyWith(completed);
			}
		}
	}

	/// <summary>
	/// A minimal IProjectionStateHandler that counts events per partition.
	/// State format: {"count":N}
	/// </summary>
	private sealed class CountingStateHandler : IProjectionStateHandler {
		private int _count;

		public void Load(string state) {
			if (string.IsNullOrEmpty(state) || state == "{}") {
				_count = 0;
				return;
			}

			try {
				using var doc = JsonDocument.Parse(state);
				if (doc.RootElement.TryGetProperty("count", out var countProp))
					_count = countProp.GetInt32();
				else
					_count = 0;
			} catch {
				_count = 0;
			}
		}

		public void LoadShared(string state) { }
		public void Initialize() { }
		public void InitializeShared() { }

		public string GetStatePartition(CheckpointTag eventPosition, string category, ProjectionResolvedEvent data) {
			return data.EventStreamId;
		}

		public bool ProcessEvent(
			string partition,
			CheckpointTag eventPosition,
			string category,
			ProjectionResolvedEvent @event,
			out string newState,
			out string newSharedState,
			out EmittedEventEnvelope[] emittedEvents) {
			_count++;
			newState = $"{{\"count\":{_count}}}";
			newSharedState = null;
			emittedEvents = null;
			return true;
		}

		public bool ProcessPartitionCreated(
			string partition, CheckpointTag createPosition, ProjectionResolvedEvent @event,
			out EmittedEventEnvelope[] emittedEvents) {
			emittedEvents = null;
			return false;
		}

		public bool ProcessPartitionDeleted(string partition, CheckpointTag deletePosition, out string newState) {
			newState = null;
			return false;
		}

		public string TransformStateToResult() => null;

		public IQuerySources GetSourceDefinition() {
			return new QuerySourcesDefinition {
				AllStreams = true,
				AllEvents = true
			};
		}

		public void Dispose() { }
	}

	#endregion

	/// <summary>
	/// Helper to create an EventRecord suitable for test use.
	/// </summary>
	private static EventRecord CreateEventRecord(string streamId, long eventNumber, long logPosition, string eventType, string data) {
		return new EventRecord(
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
			metadata: Encoding.UTF8.GetBytes("{}"));
	}

	/// <summary>
	/// Helper to create a CoreResolvedEvent from an EventRecord.
	/// </summary>
	private static CoreResolvedEvent CreateResolvedEvent(string streamId, long eventNumber, long logPosition, string eventType = "TestEvent", string data = "{}") {
		var record = CreateEventRecord(streamId, eventNumber, logPosition, eventType, data);
		return CoreResolvedEvent.ForUnresolvedEvent(record, logPosition);
	}

	[Test]
	public async Task processes_events_and_writes_checkpoint() {
		// Arrange: 10 events across 2 streams
		var events = new CoreResolvedEvent[10];
		for (int i = 0; i < 10; i++) {
			var stream = i % 2 == 0 ? "stream-A" : "stream-B";
			var pos = (i + 1) * 100L;
			events[i] = CreateResolvedEvent(stream, i / 2, pos);
		}

		var readStrategy = new FakeReadStrategy(events);
		var publisher = new CapturingPublisher();
		var stateHandler = new CountingStateHandler();
		var user = new ClaimsPrincipal(new ClaimsIdentity());

		var config = new ProjectionEngineV2Config {
			ProjectionName = "test-projection",
			SourceDefinition = stateHandler.GetSourceDefinition(),
			StateHandler = stateHandler,
			PartitionCount = 1,
			// Set CheckpointAfterMs to 0 so time-based check always passes
			CheckpointAfterMs = 0,
			// Checkpoint after 5 events
			CheckpointHandledThreshold = 5,
			CheckpointUnhandledBytesThreshold = long.MaxValue
		};

		var engine = new ProjectionEngineV2(config, readStrategy, publisher, user);

		// Act
		await engine.Start(new TFPos(0, 0), CancellationToken.None);

		// Wait for the engine to finish processing (read strategy completes after 10 events)
		// The engine's internal _runTask will complete when the read loop finishes and
		// all partition processors drain. We wait via DisposeAsync which cancels and waits.
		// But we need the engine to finish naturally first.
		// Give it some time to process, then dispose.
		var timeout = Task.Delay(TimeSpan.FromSeconds(10));
		while (!engine.IsFaulted) {
			// Check if we got at least one write
			if (publisher.Messages.Count > 0) {
				// Give a bit more time for any additional writes to come through
				await Task.Delay(200);
				break;
			}

			if (timeout.IsCompleted)
				break;

			await Task.Delay(50);
		}

		await engine.DisposeAsync();

		// Assert: engine should not have faulted
		Assert.That(engine.IsFaulted, Is.False,
			$"Engine should not fault. Exception: {engine.FaultException}");

		// Assert: publisher should have received at least one WriteEvents message
		var writeMessages = new List<ClientMessage.WriteEvents>();
		foreach (var msg in publisher.Messages) {
			if (msg is ClientMessage.WriteEvents w)
				writeMessages.Add(w);
		}

		Assert.That(writeMessages.Count, Is.GreaterThanOrEqualTo(1),
			"At least one checkpoint write should have been published");

		// Verify the first write has the expected structure
		var firstWrite = writeMessages[0];

		// Should have at least a checkpoint stream
		var streamIds = new List<string>();
		for (int i = 0; i < firstWrite.EventStreamIds.Length; i++)
			streamIds.Add(firstWrite.EventStreamIds.Span[i]);

		Assert.That(streamIds, Does.Contain("$projections-test-projection-checkpoint"),
			"Write should include the checkpoint stream");

		// Verify events contain a $ProjectionCheckpoint event
		bool hasCheckpointEvent = false;
		for (int i = 0; i < firstWrite.Events.Length; i++) {
			if (firstWrite.Events.Span[i].EventType == "$ProjectionCheckpoint") {
				hasCheckpointEvent = true;
				break;
			}
		}

		Assert.That(hasCheckpointEvent, Is.True,
			"Write should contain a $ProjectionCheckpoint event");
	}

	[Test]
	public async Task checkpoint_contains_partition_state() {
		// Arrange: 10 events all on the same stream
		var events = new CoreResolvedEvent[10];
		for (int i = 0; i < 10; i++) {
			var pos = (i + 1) * 100L;
			events[i] = CreateResolvedEvent("stream-X", i, pos, data: $"{{\"value\":{i}}}");
		}

		var readStrategy = new FakeReadStrategy(events);
		var publisher = new CapturingPublisher();
		var stateHandler = new CountingStateHandler();
		var user = new ClaimsPrincipal(new ClaimsIdentity());

		// Use ByStreams so partition key = stream name
		var sourceDef = new QuerySourcesDefinition {
			AllStreams = true,
			AllEvents = true,
			ByStreams = true
		};

		var config = new ProjectionEngineV2Config {
			ProjectionName = "state-test",
			SourceDefinition = sourceDef,
			StateHandler = stateHandler,
			PartitionCount = 1,
			CheckpointAfterMs = 0,
			CheckpointHandledThreshold = 5,
			CheckpointUnhandledBytesThreshold = long.MaxValue
		};

		var engine = new ProjectionEngineV2(config, readStrategy, publisher, user);

		// Act
		await engine.Start(new TFPos(0, 0), CancellationToken.None);

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

		Assert.That(engine.IsFaulted, Is.False,
			$"Engine should not fault. Exception: {engine.FaultException}");

		// Find the last WriteEvents message (contains final state)
		ClientMessage.WriteEvents lastWrite = null;
		foreach (var msg in publisher.Messages) {
			if (msg is ClientMessage.WriteEvents w)
				lastWrite = w;
		}

		Assert.That(lastWrite, Is.Not.Null, "Should have at least one write");

		// Check that the write contains state events
		var streamIds = new List<string>();
		for (int i = 0; i < lastWrite.EventStreamIds.Length; i++)
			streamIds.Add(lastWrite.EventStreamIds.Span[i]);

		// Should contain the state stream for partition "stream-X"
		Assert.That(streamIds, Does.Contain("$projections-state-test-stream-X-result"),
			"Write should include the partition state stream");

		// Verify a "Result" event exists
		bool hasResultEvent = false;
		for (int i = 0; i < lastWrite.Events.Length; i++) {
			if (lastWrite.Events.Span[i].EventType == "Result") {
				hasResultEvent = true;
				// Verify the state data contains a count
				var stateJson = Encoding.UTF8.GetString(lastWrite.Events.Span[i].Data);
				Assert.That(stateJson, Does.Contain("\"count\":"),
					"Result event should contain a count in the state JSON");
				break;
			}
		}

		Assert.That(hasResultEvent, Is.True,
			"Write should contain a Result event with partition state");
	}

	[Test]
	public async Task checkpoint_position_reflects_last_processed_event() {
		// Arrange: 6 events so we get at least one checkpoint at 5
		var events = new CoreResolvedEvent[6];
		for (int i = 0; i < 6; i++) {
			var pos = (i + 1) * 100L;
			events[i] = CreateResolvedEvent("stream-Z", i, pos);
		}

		var readStrategy = new FakeReadStrategy(events);
		var publisher = new CapturingPublisher();
		var stateHandler = new CountingStateHandler();
		var user = new ClaimsPrincipal(new ClaimsIdentity());

		var config = new ProjectionEngineV2Config {
			ProjectionName = "pos-test",
			SourceDefinition = stateHandler.GetSourceDefinition(),
			StateHandler = stateHandler,
			PartitionCount = 1,
			CheckpointAfterMs = 0,
			CheckpointHandledThreshold = 5,
			CheckpointUnhandledBytesThreshold = long.MaxValue
		};

		var engine = new ProjectionEngineV2(config, readStrategy, publisher, user);

		// Act
		await engine.Start(new TFPos(0, 0), CancellationToken.None);

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

		Assert.That(engine.IsFaulted, Is.False,
			$"Engine should not fault. Exception: {engine.FaultException}");

		// Get the first checkpoint write
		ClientMessage.WriteEvents firstWrite = null;
		foreach (var msg in publisher.Messages) {
			if (msg is ClientMessage.WriteEvents w) {
				firstWrite = w;
				break;
			}
		}

		Assert.That(firstWrite, Is.Not.Null, "Should have at least one write");

		// Find the $ProjectionCheckpoint event and parse its data
		for (int i = 0; i < firstWrite.Events.Length; i++) {
			var evt = firstWrite.Events.Span[i];
			if (evt.EventType == "$ProjectionCheckpoint") {
				var json = Encoding.UTF8.GetString(evt.Data);
				using var doc = JsonDocument.Parse(json);

				Assert.That(doc.RootElement.TryGetProperty("commitPosition", out var commitProp), Is.True,
					"Checkpoint should have commitPosition");
				Assert.That(doc.RootElement.TryGetProperty("preparePosition", out var prepareProp), Is.True,
					"Checkpoint should have preparePosition");

				// The position should be non-zero (events start at position 100)
				Assert.That(commitProp.GetInt64(), Is.GreaterThan(0),
					"Checkpoint commitPosition should be greater than zero");
				Assert.That(prepareProp.GetInt64(), Is.GreaterThan(0),
					"Checkpoint preparePosition should be greater than zero");
				break;
			}
		}
	}
}
