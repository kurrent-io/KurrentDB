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
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SchemaFormat = KurrentDB.Protocol.V2.Streams.SchemaFormat;
using SchemaInfo = KurrentDB.Protocol.V2.Streams.SchemaInfo;

namespace KurrentDB.Projections.V2.Tests.Integration;

public class AccountBalancerSpecTests {
	[ClassDataSource<ProjectionsNodeFixture>(Shared = SharedType.PerTestSession)]
	public required ProjectionsNodeFixture Fixture { get; init; }

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
		private readonly ConcurrentQueue<Message> _messages = new();
		public IReadOnlyCollection<Message> Messages => _messages;

		public void Publish(Message message) {
			_messages.Enqueue(message);

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

	[Test]
	[Timeout(30_000)]
	public async Task account_balancer_bistate_projection_processes_spec_correctly(CancellationToken ct) {
		var testId = Guid.NewGuid().ToString("N")[..8];

		// 1. Read spec files
		var specsDir = Path.Combine(AppContext.BaseDirectory, "Specs");
		var projectionSource = await File.ReadAllTextAsync(Path.Combine(specsDir, "account-balancer.js"), ct);
		var specJson = await File.ReadAllTextAsync(Path.Combine(specsDir, "account-spec.json"), ct);
		using var specDoc = JsonDocument.Parse(specJson);

		// 2. Write events from the spec to categorized streams using a unique category
		var category = $"acct{testId}";
		var rewrittenSource = projectionSource.Replace(
			"fromCategory(\"transaction\")",
			$"fromCategory(\"{category}\")");

		var streamMapping = new Dictionary<string, string>();
		foreach (var inputStream in specDoc.RootElement.GetProperty("input").EnumerateArray()) {
			var specStreamId = inputStream.GetProperty("streamId").GetString()!;
			var suffix = specStreamId.Split('-').Last();
			var actualStreamId = $"{category}-{suffix}";
			streamMapping[specStreamId] = actualStreamId;

			var events = new List<(string eventType, string json)>();
			foreach (var evt in inputStream.GetProperty("events").EnumerateArray()) {
				var eventType = evt.GetProperty("eventType").GetString()!;
				var data = JObject.Parse(evt.GetProperty("data").GetRawText()).ToString(Formatting.Indented);
				events.Add((eventType, data));
			}

			await Fixture.StreamsClient.AppendAsync(
				CreateAppendRequest(actualStreamId, events.ToArray()), ct);
		}

		// Wait for $by_category to index
		await Task.Delay(3000, ct);

		// 4. Create state handler factory
		var trackers = ProjectionTrackers.NoOp;

		using var sourceHandler = CreateStateHandler();
		var sourceDefinition = sourceHandler.GetSourceDefinition();

		// Verify it's a biState projection
		await Assert.That(sourceDefinition.IsBiState).IsTrue();
		await Assert.That(sourceDefinition.ByCustomPartitions).IsTrue();

		// 5. Create read strategy
		var adminUser = new ClaimsPrincipal(new ClaimsIdentity(new[] {
			new Claim(ClaimTypes.Name, "admin"),
			new Claim(ClaimTypes.Role, "$admins")
		}, "test"));

		var readStrategy = ReadStrategyFactory.Create(
			sourceDefinition, Fixture.MainQueue, adminUser);

		// 6. Set up capturing publisher
		var capturingPublisher = new CapturingPublisher(Fixture.MainQueue);

		// 7. Configure and start engine
		var projectionName = $"spec-test-{testId}";
		var config = new ProjectionEngineV2Config {
			ProjectionName = projectionName,
			SourceDefinition = sourceDefinition,
			StateHandlerFactory = CreateStateHandler,
			MaxPartitionStateCacheSize = 1000,
			CheckpointAfterMs = 0,
			CheckpointHandledThreshold = 1,
			CheckpointUnhandledBytesThreshold = long.MaxValue,
			EmitEnabled = true
		};

		var engine = new ProjectionEngineV2(
			config, readStrategy, new SystemClient(capturingPublisher), adminUser);

		engine.Start(new TFPos(0, 0));

		// 8. Wait for checkpoint writes
		var deadline = Task.Delay(TimeSpan.FromSeconds(10));
		while (!engine.IsFaulted) {
			var writeCount = capturingPublisher.Messages
				.OfType<ClientMessage.WriteEvents>()
				.Count();
			if (writeCount > 0) {
				await Task.Delay(2000);
				break;
			}
			if (deadline.IsCompleted) break;
			await Task.Delay(100);
		}

		await engine.DisposeAsync();

		// 9. Verify the engine didn't fault
		if (engine.IsFaulted)
			throw new Exception($"Engine faulted: {engine.FaultException}");

		// 10. Extract all writes
		var writes = capturingPublisher.Messages
			.OfType<ClientMessage.WriteEvents>()
			.ToList();

		await Assert.That(writes.Count).IsGreaterThanOrEqualTo(1);

		// Collect all state results by stream
		var statesByStream = new Dictionary<string, string>();
		foreach (var w in writes) {
			var evts = w.Events.ToArray();
			for (int i = 0; i < evts.Length; i++) {
				if (evts[i].EventType == ProjectionEventTypes.ProjectionStateV2) {
					var streamIdx = w.EventStreamIndexes.Span[i];
					var streamId = w.EventStreamIds.Span[streamIdx];
					statesByStream[streamId] = Encoding.UTF8.GetString(evts[i].Data);
				}
			}
		}

		// 11. Verify final states from the spec
		// The spec's final expected states (after all events processed):
		// From the last non-skipped event in each sequence, we get the final states.
		// Let's collect the last expected state per partition from the spec.
		var expectedFinalStates = new Dictionary<string, string?>(); // partition -> state json
		foreach (var inputStream in specDoc.RootElement.GetProperty("input").EnumerateArray()) {
			foreach (var evt in inputStream.GetProperty("events").EnumerateArray()) {
				if (evt.TryGetProperty("skip", out var skip) && skip.GetBoolean())
					continue;
				if (!evt.TryGetProperty("states", out var states))
					continue;
				foreach (var state in states.EnumerateArray()) {
					var partition = state.GetProperty("partition").GetString()!;
					var stateNode = state.GetProperty("state");
					expectedFinalStates[partition] = stateNode.ValueKind == JsonValueKind.Null
						? null
						: stateNode.GetRawText();
				}
			}
		}

		// Verify ESDBB-01 final state: balance=550, debit=150, description="bill payment"
		var esdbb01StreamId = $"$projections-{projectionName}-ESDBB-01-state";
		await Assert.That(statesByStream.ContainsKey(esdbb01StreamId)).IsTrue();
		var esdbb01State = JObject.Parse(statesByStream[esdbb01StreamId]);
		var expectedEsdbb01 = JObject.Parse(expectedFinalStates["ESDBB-01"]!);
		AssertJsonEquivalent(expectedEsdbb01, esdbb01State);

		// Verify ESDBB-S01 final state: balance=300, credit=300, description="transfer to savings"
		var esdbbS01StreamId = $"$projections-{projectionName}-ESDBB-S01-state";
		await Assert.That(statesByStream.ContainsKey(esdbbS01StreamId)).IsTrue();
		var esdbbS01State = JObject.Parse(statesByStream[esdbbS01StreamId]);
		var expectedEsdbbS01 = JObject.Parse(expectedFinalStates["ESDBB-S01"]!);
		AssertJsonEquivalent(expectedEsdbbS01, esdbbS01State);

		// Verify shared state (partition ""): numberOfAccounts=2, totalBalance=850, description="bill payment"
		var sharedStreamId = $"$projections-{projectionName}-state";
		await Assert.That(statesByStream.ContainsKey(sharedStreamId)).IsTrue();
		var sharedState = JObject.Parse(statesByStream[sharedStreamId]);
		var expectedShared = JObject.Parse(expectedFinalStates[""]!);
		AssertJsonEquivalent(expectedShared, sharedState);

		// 12. Verify "description" partition state is null (filtered by the projection)
		// The "description" partition gets s[0] = null in $created.
		// The V2 engine still writes this state. Let's verify it's null.
		var descStreamId = $"$projections-{projectionName}-description-state";
		if (statesByStream.TryGetValue(descStreamId, out var descState)) {
			// The state should be "null" (JS null serialized)
			await Assert.That(descState).IsEqualTo("null");
		}

		// 13. Verify no external account states were written
		var allStreamIds = writes
			.SelectMany(w => Enumerable.Range(0, w.EventStreamIds.Length)
				.Select(i => w.EventStreamIds.Span[i]))
			.ToList();

		var externalStreams = allStreamIds
			.Where(s => s.Contains("External") || s.Contains("EXTERNAL"))
			.ToList();
		await Assert.That(externalStreams.Count).IsEqualTo(0);
		return;

		IProjectionStateHandler CreateStateHandler() => new JintProjectionStateHandler(
			rewrittenSource,
			enableContentTypeValidation: false,
			compilationTimeout: TimeSpan.FromSeconds(5),
			executionTimeout: TimeSpan.FromSeconds(5),
			new(trackers.GetExecutionTrackerForProjection("spec-test")),
			new(trackers.GetSerializationTrackerForProjection("spec-test")));
	}

	static void AssertJsonEquivalent(JObject expected, JObject actual) {
		foreach (var prop in expected.Properties()) {
			var actualProp = actual.Property(prop.Name);
			if (actualProp == null)
				throw new Exception($"Missing property '{prop.Name}' in actual. Expected: {expected}, Actual: {actual}");

			switch (prop.Value.Type) {
				case JTokenType.Integer or JTokenType.Float: {
					var expectedVal = prop.Value.Value<double>();
					var actualVal = actualProp.Value.Value<double>();
					if (Math.Abs(expectedVal - actualVal) > 0.001)
						throw new Exception($"Property '{prop.Name}': expected {expectedVal}, got {actualVal}");
					break;
				}
				case JTokenType.String: {
					var expectedStr = prop.Value.Value<string>();
					var actualStr = actualProp.Value.Value<string>();
					if (expectedStr != actualStr)
						throw new Exception($"Property '{prop.Name}': expected '{expectedStr}', got '{actualStr}'");
					break;
				}
			}
		}
	}
}
