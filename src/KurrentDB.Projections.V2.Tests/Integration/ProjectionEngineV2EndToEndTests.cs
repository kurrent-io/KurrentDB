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
using KurrentDB.Projections.Core.Services.Processing.V2.ReadStrategies;
using KurrentDB.Projections.V2.Tests.Fixtures;
using KurrentDB.Protocol.V2.Streams;
using SchemaFormat = KurrentDB.Protocol.V2.Streams.SchemaFormat;
using SchemaInfo = KurrentDB.Protocol.V2.Streams.SchemaInfo;

namespace KurrentDB.Projections.V2.Tests.Integration;

public class ProjectionEngineV2EndToEndTests {
	[ClassDataSource<ProjectionsNodeFixture>(Shared = SharedType.PerTestSession)]
	public required ProjectionsNodeFixture Fixture { get; init; }

	// The projection:
	// - Subscribes to category "order" (reads from $ce-order via filtered all with stream prefix "order-")
	// - Partitions state by stream (foreachStream)
	// - Counts events per stream, tracks total amount from OrderPlaced events
	// - Emits a summary event to "order-summary-{streamId}" when an OrderPlaced event is received
	// - Ignores non-matching event types (OrderNoteAdded should not emit)
	const string ProjectionQuery = @"
fromCategory('order')
.foreachStream()
.when({
	$init: function() {
		return { count: 0, totalAmount: 0 };
	},
	OrderPlaced: function(s, e) {
		s.count++;
		s.totalAmount += e.data.amount;
		emit('order-summary-' + e.streamId, 'OrderSummaryUpdated', {
			stream: e.streamId,
			count: s.count,
			totalAmount: s.totalAmount
		});
		return s;
	},
	OrderShipped: function(s, e) {
		s.count++;
		return s;
	}
})
";

	static AppendRequest CreateAppendRequest(string stream, params (string eventType, string json)[] events) {
		var request = new AppendRequest {
			Stream = stream,
			ExpectedRevision = -2 // Any
		};
		foreach (var (eventType, json) in events) {
			request.Records.Add(new AppendRecord {
				RecordId = Guid.NewGuid().ToString(),
				Data = ByteString.CopyFromUtf8(json),
				Schema = new SchemaInfo {
					Name = eventType,
					Format = SchemaFormat.Json
				}
			});
		}
		return request;
	}

	sealed class CapturingPublisher(IPublisher inner) : IPublisher {
		public ConcurrentBag<Message> Messages { get; } = new();

		public void Publish(Message message) {
			Messages.Add(message);

			if (message is ClientMessage.WriteEvents w) {
				// Reply with success so checkpoint writes complete
				var first = new long[w.EventStreamIds.Length];
				var last = new long[w.EventStreamIds.Length];
				w.Envelope.ReplyWith(new ClientMessage.WriteEventsCompleted(
					w.CorrelationId, first, last, preparePosition: 0, commitPosition: 0));
			} else {
				// Forward non-write messages to the real bus (for reads)
				inner.Publish(message);
			}
		}
	}

	[Test]
	[Timeout(30_000)]
	public async Task projection_with_category_partitioned_state_and_emit(CancellationToken ct) {
		var testId = Guid.NewGuid().ToString("N")[..8];

		// 1. Write events to categorized streams
		// Matching events: OrderPlaced and OrderShipped to "order-{id}" streams
		// Non-matching events: different category ("invoice-{id}") that should be filtered out
		await Fixture.StreamsClient.AppendAsync(
			CreateAppendRequest($"order-{testId}-1",
				("OrderPlaced", """{"amount": 100.50, "item": "Widget"}"""),
				("OrderShipped", """{"trackingId": "TRK-001"}"""),
				("OrderPlaced", """{"amount": 200.00, "item": "Gadget"}""")),
			ct);

		await Fixture.StreamsClient.AppendAsync(
			CreateAppendRequest($"order-{testId}-2",
				("OrderPlaced", """{"amount": 50.00, "item": "Doohickey"}""")),
			ct);

		// Non-matching: different category
		await Fixture.StreamsClient.AppendAsync(
			CreateAppendRequest($"invoice-{testId}-1",
				("InvoiceCreated", """{"total": 999.99}""")),
			ct);

		// Non-matching: unrelated event type in matching stream (should be counted but not emit)
		await Fixture.StreamsClient.AppendAsync(
			CreateAppendRequest($"order-{testId}-1",
				("OrderNoteAdded", """{"note": "Please gift wrap"}""")),
			ct);

		// Wait for standard $by_category projection to index the events
		await Task.Delay(3000);

		// 2. Create JS state handler factory from the projection query
		var trackers = ProjectionTrackers.NoOp;
		IProjectionStateHandler CreateStateHandler() => new JintProjectionStateHandler(
			ProjectionQuery,
			enableContentTypeValidation: false,
			compilationTimeout: TimeSpan.FromSeconds(5),
			executionTimeout: TimeSpan.FromSeconds(5),
			new(trackers.GetExecutionTrackerForProjection("e2e-test")),
			new(trackers.GetSerializationTrackerForProjection("e2e-test")));

		using var sourceHandler = CreateStateHandler();
		var sourceDefinition = sourceHandler.GetSourceDefinition();

		// 3. Create a real read strategy that reads from the node's bus
		var readStrategy = ReadStrategyFactory.Create(
			sourceDefinition,
			Fixture.MainQueue,
			new ClaimsPrincipal(new ClaimsIdentity(new[] {
				new Claim(ClaimTypes.Name, "admin"),
				new Claim(ClaimTypes.Role, "$admins")
			}, "test")));

		// 4. Set up capturing publisher (intercepts writes, forwards reads)
		var capturingPublisher = new CapturingPublisher(Fixture.MainQueue);

		// 5. Configure and start the V2 engine
		var config = new ProjectionEngineV2Config {
			ProjectionName = $"e2e-test-{testId}",
			SourceDefinition = sourceDefinition,
			StateHandlerFactory = CreateStateHandler,
			CheckpointAfterMs = 0,
			CheckpointHandledThreshold = 3,
			CheckpointUnhandledBytesThreshold = long.MaxValue,
			EmitEnabled = true
		};

		var engine = new ProjectionEngineV2(
			config,
			readStrategy,
			new SystemClient(capturingPublisher),
			new ClaimsPrincipal(new ClaimsIdentity(new[] {
				new Claim(ClaimTypes.Name, "admin"),
				new Claim(ClaimTypes.Role, "$admins")
			}, "test")));

		engine.Start(new TFPos(0, 0));

		// 6. Wait for checkpoint writes to appear
		var deadline = Task.Delay(TimeSpan.FromSeconds(10));
		while (!engine.IsFaulted) {
			var writeCount = capturingPublisher.Messages
				.OfType<ClientMessage.WriteEvents>()
				.Count();
			if (writeCount > 0) {
				// Give a little extra time for all events to be processed
				await Task.Delay(2000);
				break;
			}
			if (deadline.IsCompleted) break;
			await Task.Delay(100);
		}

		await engine.DisposeAsync();

		// 7. Verify the engine didn't fault
		await Assert.That(engine.IsFaulted).IsFalse();

		// 8. Verify checkpoint writes occurred
		var writes = capturingPublisher.Messages
			.OfType<ClientMessage.WriteEvents>()
			.ToList();

		await Assert.That(writes.Count).IsGreaterThanOrEqualTo(1);

		// 9. Collect all events from all writes
		var allStreamIds = writes
			.SelectMany(w => Enumerable.Range(0, w.EventStreamIds.Length)
				.Select(i => w.EventStreamIds.Span[i]))
			.ToList();

		var allEvents = writes
			.SelectMany(w => w.Events.ToArray())
			.ToList();

		// 10. Verify checkpoint event exists
		var checkpointEvents = allEvents.Where(e => e.EventType == ProjectionEventTypes.ProjectionCheckpointV2).ToList();
		await Assert.That(checkpointEvents.Count).IsGreaterThanOrEqualTo(1);

		// Verify checkpoint contains valid position
		var cpJson = Encoding.UTF8.GetString(checkpointEvents.Last().Data);
		using var cpDoc = JsonDocument.Parse(cpJson);
		await Assert.That(cpDoc.RootElement.GetProperty("commitPosition").GetInt64()).IsGreaterThan(0);

		// 11. Verify partition state for order-{testId}-1
		// Expected: count=3 (OrderPlaced + OrderShipped + OrderPlaced), totalAmount=300.50
		// Note: OrderNoteAdded is not handled by any explicit handler, so it's ignored
		var order1StreamId = $"$projections-e2e-test-{testId}-order-{testId}-1-state";
		var order1Results = allEvents
			.Where((e, idx) => e.EventType == ProjectionEventTypes.ProjectionStateV2 &&
				writes.Any(w => {
					var evts = w.Events.ToArray();
					var evtIdx = Array.IndexOf(evts, e);
					if (evtIdx < 0) return false;
					var streamIdx = w.EventStreamIndexes.Span[evtIdx];
					return w.EventStreamIds.Span[streamIdx] == order1StreamId;
				}))
			.ToList();

		// The state should exist
		await Assert.That(order1Results.Count).IsGreaterThanOrEqualTo(1);

		var lastOrder1State = Encoding.UTF8.GetString(order1Results.Last().Data);
		using var stateDoc = JsonDocument.Parse(lastOrder1State);
		var count = stateDoc.RootElement.GetProperty("count").GetInt32();
		var totalAmount = stateDoc.RootElement.GetProperty("totalAmount").GetDouble();

		// order-{testId}-1 has: OrderPlaced(100.50), OrderShipped, OrderPlaced(200.00) = 3 events, 300.50 total
		await Assert.That(count).IsEqualTo(3);
		await Assert.That(totalAmount).IsEqualTo(300.50);

		// 12. Verify partition state for order-{testId}-2
		var order2StreamId = $"$projections-e2e-test-{testId}-order-{testId}-2-state";
		var order2Results = allEvents
			.Where((e, idx) => e.EventType == ProjectionEventTypes.ProjectionStateV2 &&
				writes.Any(w => {
					var evts = w.Events.ToArray();
					var evtIdx = Array.IndexOf(evts, e);
					if (evtIdx < 0) return false;
					var streamIdx = w.EventStreamIndexes.Span[evtIdx];
					return w.EventStreamIds.Span[streamIdx] == order2StreamId;
				}))
			.ToList();

		await Assert.That(order2Results.Count).IsGreaterThanOrEqualTo(1);

		var lastOrder2State = Encoding.UTF8.GetString(order2Results.Last().Data);
		using var state2Doc = JsonDocument.Parse(lastOrder2State);
		await Assert.That(state2Doc.RootElement.GetProperty("count").GetInt32()).IsEqualTo(1);
		await Assert.That(state2Doc.RootElement.GetProperty("totalAmount").GetDouble()).IsEqualTo(50.00);

		// 13. Verify emitted events exist (OrderSummaryUpdated)
		var emittedEvents = allEvents.Where(e => e.EventType == "OrderSummaryUpdated").ToList();
		await Assert.That(emittedEvents.Count).IsGreaterThanOrEqualTo(1);

		// Verify emitted event content
		var emittedJson = Encoding.UTF8.GetString(emittedEvents.First().Data);
		using var emittedDoc = JsonDocument.Parse(emittedJson);
		await Assert.That(emittedDoc.RootElement.TryGetProperty("stream", out _)).IsTrue();
		await Assert.That(emittedDoc.RootElement.TryGetProperty("totalAmount", out _)).IsTrue();

		// 14. Verify emitted events target the correct streams
		var emittedStreamIds = allStreamIds
			.Where(s => s.StartsWith("order-summary-"))
			.Distinct()
			.ToList();
		await Assert.That(emittedStreamIds.Count).IsGreaterThanOrEqualTo(1);

		// 15. Verify invoice events were NOT processed (no invoice-related state)
		var invoiceStreamIds = allStreamIds
			.Where(s => s.Contains("invoice"))
			.ToList();
		await Assert.That(invoiceStreamIds.Count).IsEqualTo(0);
	}
}
