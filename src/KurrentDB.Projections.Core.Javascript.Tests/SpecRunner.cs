// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using KurrentDB.Core.Data;
using KurrentDB.Core.TransactionLog.LogRecords;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Metrics;
using KurrentDB.Projections.Core.Services.Interpreted;
using KurrentDB.Projections.Core.Services.Processing;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using KurrentDB.Projections.Core.Services.Processing.Emitting.EmittedEvents;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;
using ResolvedEvent = KurrentDB.Projections.Core.Services.Processing.ResolvedEvent;

namespace KurrentDB.Projections.Core.Javascript.Tests;

public class SpecRunner {
	private readonly ITestOutputHelper _output;

	public SpecRunner(ITestOutputHelper output) {
		_output = output;
	}

	private static readonly Encoding Utf8NoBom = new UTF8Encoding(false, true);

	public static IEnumerable<object[]> GetTestCases() {
		var assembly = typeof(SpecRunner).Assembly;
		var specs = assembly.GetManifestResourceNames().Where(x => x.EndsWith("-spec.json"));
		foreach (var spec in specs) {
			JsonDocument doc;
			using (var stream = assembly.GetManifestResourceStream(spec)) {
				doc = JsonDocument.Parse(stream!);
			}

			var projection = doc.RootElement.GetProperty("projection").GetString();
			Assert.False(string.IsNullOrWhiteSpace(projection), "projection name cannot be empty");
			var projectionSourceName = assembly.GetManifestResourceNames()
				.SingleOrDefault(x => x.EndsWith($"{projection}.js"));
			Assert.NotNull(projectionSourceName);
			string source;
			using (var stream = assembly.GetManifestResourceStream(projectionSourceName!)) {
				Assert.NotNull(stream);
				using (var sr = new StreamReader(stream!)) {
					source = sr.ReadToEnd();
				}
			}

			List<InputEventSequence> sequences = new();
			foreach (var inputEvent in doc.RootElement.GetProperty("input").EnumerateArray()) {
				var stream = inputEvent.GetProperty("streamId").GetString();
				Assert.NotNull(stream);
				var sequence = new InputEventSequence(stream!);
				sequences.Add(sequence);
				foreach (var e in inputEvent.GetProperty("events").EnumerateArray()) {
					var et = e.GetProperty("eventType").GetString();
					Assert.NotNull(et);
					bool skip = false;
					if (e.TryGetProperty("skip", out var skipElement)) {
						skip = skipElement.GetBoolean();
					}

					var initializedPartitions = new List<string>();
					if (e.TryGetProperty("initializedPartitions", out var partitions)) {
						foreach (var element in partitions.EnumerateArray()) {
							initializedPartitions.Add(element.GetString()!);
						}
					}
					var expectedStates = new Dictionary<string, string?>();
					var expectedResults = new Dictionary<string, string?>();
					int stateCount = 0;
					if (e.TryGetProperty("states", out var states)) {
						foreach (var state in states.EnumerateArray()) {
							stateCount++;
							var partition = state.GetProperty("partition").GetString()!;
							var expectedStateNode = state.GetProperty("state");
							var expectedState = expectedStateNode.ValueKind == JsonValueKind.Null ? null : expectedStateNode.GetRawText();
							if (!expectedStates.TryAdd(partition, expectedState)) throw new InvalidOperationException("Duplicate expected state");

							if (state.TryGetProperty("result", out var expectedResultNode )) {
								var expectedResult = expectedResultNode.ValueKind == JsonValueKind.Null
									? null
									: expectedResultNode.GetRawText();
								if (!expectedResults.TryAdd(partition, expectedResult))
									throw new InvalidOperationException("Duplicate expected result");
							}
						}
					}


					if (stateCount > 2)
						throw new InvalidOperationException("Cannot specify more than 2 states");

					sequence.Events.Add(new InputEvent(et!
							, e.GetProperty("data").GetRawText()
							, e.TryGetProperty("metadata", out var metadata) ? metadata.GetRawText() : null
							, initializedPartitions
							, expectedStates
							, expectedResults
							, skip
							, e.TryGetProperty("eventId", out var idElement) && idElement.TryGetGuid(out var id) ? id : Guid.NewGuid()
							));
				}
			}

			var output = doc.RootElement.GetProperty("output");
			var sdb = new SourceDefinitionBuilder();
			var config = output.GetProperty("config");
			foreach (var item in config.EnumerateObject()) {
				switch (item.Name) {
					case "definesStateTransform":
						if (item.Value.GetBoolean())
							sdb.SetDefinesStateTransform();
						break;
					case "handlesDeletedNotifications":
						sdb.SetHandlesStreamDeletedNotifications(item.Value.GetBoolean());
						break;
					case "producesResults":
						if (item.Value.GetBoolean())
							sdb.SetOutputState();
						break;
					case "definesFold":
						if (item.Value.GetBoolean())
							sdb.SetDefinesFold();
						break;
					case "resultStreamName":
						sdb.SetResultStreamNameOption(item.Value.GetString());
						break;
					case "partitionResultStreamNamePattern":
						sdb.SetPartitionResultStreamNamePatternOption(item.Value.GetString());
						break;
					case "$includeLinks":
						sdb.SetIncludeLinks(item.Value.GetBoolean());
						break;
					case "reorderEvents":
						sdb.SetReorderEvents(item.Value.GetBoolean());
						break;
					case "processingLag":
						sdb.SetProcessingLag(item.Value.GetInt32());
						break;
					case "biState":
						sdb.SetIsBiState(item.Value.GetBoolean());
						break;
					case "categories":
						foreach (var c in item.Value.EnumerateArray()) {
							sdb.FromCategory(c.GetString());
						}
						break;
					case "partitioned":
						if (item.Value.GetBoolean())
							sdb.SetByCustomPartitions();
						break;
					case "events":
						foreach (var e in item.Value.EnumerateArray()) {
							sdb.IncludeEvent(e.GetString());
						}
						break;
					case "allEvents":
						if (item.Value.GetBoolean()) {
							sdb.AllEvents();
						} else {
							sdb.NotAllEvents();
						}
						break;
					case "allStreams":
						if (item.Value.GetBoolean()) {
							sdb.FromAll();
						}
						break;
					default:
						throw new Exception($"unexpected property in expected config {item.Name}");
				}
			}
#nullable disable

			List<OutputEvent> expectedEmittedEvents = new();
			if (output.TryGetProperty("emitted", out var expectedEmittedElement)) {
				foreach (var element in expectedEmittedElement.EnumerateObject()) {
					var stream = element.Name;
					foreach (var eventElement in element.Value.EnumerateArray()) {
						if (eventElement.ValueKind == JsonValueKind.String) {
							expectedEmittedEvents.Add(
								new OutputEvent(stream, "$>", eventElement.GetString(), null));
						}
					}
				}
			}
			JintProjectionStateHandler runner = null;
			IQuerySources definition = null;

			IQuerySources expectedDefinition = sdb.Build();
			yield return WithOutput($"{projection} compiles", o => {
				runner = new JintProjectionStateHandler(source, true,
					compilationTimeout: TimeSpan.FromMilliseconds(200),
					executionTimeout: TimeSpan.FromMilliseconds(200),
					jsFunctionCaller: new(IProjectionExecutionTracker.NoOp));
			});

			yield return For($"{projection} getDefinition", () => { definition = runner.GetSourceDefinition(); });
			yield return For($"{projection} qs.AllStreams", () => Assert.Equal(expectedDefinition.AllStreams, definition.AllStreams));
			yield return For($"{projection} qs.Categories", () => Assert.Equal(expectedDefinition.Categories, definition.Categories));
			yield return For($"{projection} qs.Streams", () => Assert.Equal(expectedDefinition.Streams, definition.Streams));
			yield return For($"{projection} qs.AllEvents", () => Assert.Equal(expectedDefinition.AllEvents, definition.AllEvents));
			yield return For($"{projection} qs.Events", () => Assert.Equal(expectedDefinition.Events, definition.Events));
			yield return For($"{projection} qs.ByStreams", () => Assert.Equal(expectedDefinition.ByStreams, definition.ByStreams));
			yield return For($"{projection} qs.ByCustomPartitions", () => Assert.Equal(expectedDefinition.ByCustomPartitions, definition.ByCustomPartitions));
			yield return For($"{projection} qs.DefinesStateTransform", () => Assert.Equal(expectedDefinition.DefinesStateTransform, definition.DefinesStateTransform));
			yield return For($"{projection} qs.DefinesFold", () => Assert.Equal(expectedDefinition.DefinesFold, definition.DefinesFold));
			yield return For($"{projection} qs.HandlesDeletedNotifications", () => Assert.Equal(expectedDefinition.HandlesDeletedNotifications, definition.HandlesDeletedNotifications));
			yield return For($"{projection} qs.ProducesResults", () => Assert.Equal(expectedDefinition.ProducesResults, definition.ProducesResults));
			yield return For($"{projection} qs.IsBiState", () => Assert.Equal(expectedDefinition.IsBiState, definition.IsBiState));
			yield return For($"{projection} qs.IncludeLinksOption", () => Assert.Equal(expectedDefinition.IncludeLinksOption, definition.IncludeLinksOption));
			yield return For($"{projection} qs.ResultStreamNameOption", () => Assert.Equal(expectedDefinition.ResultStreamNameOption, definition.ResultStreamNameOption));
			yield return For($"{projection} qs.PartitionResultStreamNamePatternOption", () => Assert.Equal(expectedDefinition.PartitionResultStreamNamePatternOption,
				definition.PartitionResultStreamNamePatternOption));
			yield return For($"{projection} qs.ReorderEventsOption", () => Assert.Equal(expectedDefinition.ReorderEventsOption, definition.ReorderEventsOption));
			yield return For($"{projection} qs.ProcessingLagOption", () => Assert.Equal(expectedDefinition.ProcessingLagOption, definition.ProcessingLagOption));
			yield return For($"{projection} qs.LimitingCommitPosition", () => Assert.Equal(expectedDefinition.LimitingCommitPosition, definition.LimitingCommitPosition));
			var partitionedState = new Dictionary<string, string>();
			var partitionedResult = new Dictionary<string, string>();
			var sharedStateInitialized = false;
			var revision = new Dictionary<string, long>();
			List<EmittedEventEnvelope> actualEmittedEvents = new();

			for (int i = 0; i < sequences.Count; i++) {
				var sequence = sequences[i];
				if (!revision.TryGetValue(sequence.Stream, out _)) {
					revision[sequence.Stream] = 0;
				}
				for (int j = 0; j < sequences[i].Events.Count; j++) {
					var logPosition = i * 100 + j;
					var flags = PrepareFlags.IsJson | PrepareFlags.Data;
					if (j == 0)
						flags |= PrepareFlags.TransactionBegin;
					if (j == sequence.Events.Count - 1)
						flags |= PrepareFlags.TransactionEnd;

					/*Sequence:
					Get partition if bycustom partition or by stream
						if the partition is null or an empty string skip this event (NB: need to indicate that an event should be skipped)
					load the state if it doesn't exist or tell the projection to init state
					init shared if it doesn't exist
					if init was state was called, call created
					if processing a delete, call partition deleted (NB: bistate partitions do not support deletes)
					process the event
					save any shared state if it isn't null
					save any state
					save emitted events to verify later
					*/
					var @event = sequence.Events[j];
					var body = JObject.Parse(@event.Body).ToString(Formatting.Indented);
					var metadata = Array.Empty<byte>();
					if (@event.Metadata != null) {
						metadata = Utf8NoBom.GetBytes(JObject.Parse(@event.Metadata).ToString(Formatting.Indented));
					}
					var er = new EventRecord(
						revision[sequence.Stream], logPosition, Guid.NewGuid(), @event.EventId, i, j,
						sequence.Stream, i, DateTime.Now, flags, @event.EventType,
						Utf8NoBom.GetBytes(body), metadata, []);
					var e = new ResolvedEvent(KurrentDB.Core.Data.ResolvedEvent.ForUnresolvedEvent(er, logPosition), Array.Empty<byte>());
					if (@event.Skip) {
						yield return For($"{projection} skips {er.EventNumber}@{sequence.Stream}",
							() => Assert.Null(runner.GetStatePartition(CheckpointTag.Empty, "", e)));
					} else {
						var expectedPartition = "";
						if (expectedDefinition.DefinesFold) {
							if (@event.ExpectedStates.Any()) {
								expectedPartition = @event.ExpectedStates
									.SingleOrDefault(x => string.Empty != x.Key).Key ?? string.Empty;
								if (expectedPartition != string.Empty) {
									yield return For(
										$"{projection} {er.EventNumber}@{sequence.Stream} returns expected partition",
										() => Assert.Equal(expectedPartition,
											runner.GetStatePartition(CheckpointTag.Empty, "", e)));
									foreach (var initializedState in @event.InitializedPartitions) {
										yield return For(
											$"should not have already initialized \"{initializedState}\" at {er.EventNumber}@{sequence.Stream}",
											() => Assert.DoesNotContain(initializedState,
												(IReadOnlyDictionary<string, string>)partitionedState));
									}
								}

								if (expectedDefinition.IsBiState && !sharedStateInitialized) {
									sharedStateInitialized = true;
									yield return For("initializes shared state without error", () => {
										runner.InitializeShared();
									});
								}

								if (!@event.InitializedPartitions.Contains(expectedPartition)) {
									yield return For(
										$"can load current state at {er.EventNumber}@{sequence.Stream}",
										() => {
											Assert.True(
												partitionedState.TryGetValue(expectedPartition, out var value),
												$"did not find expected state for partition{expectedPartition}");
											runner.Load(value);
										});
								} else {
									yield return For(
										$"initializes new state at {er.EventNumber}@{sequence.Stream}",
										() => {
											var ex = Record.Exception(() => runner.Initialize());
											Assert.Null(ex);
										});

									yield return For(
										$"handles created partition \"{expectedPartition}\" at {er.EventNumber}@{sequence.Stream}",
										() => {
											var result = runner.ProcessPartitionCreated(expectedPartition,
												CheckpointTag.Empty, e, out var emittedEvents);
											if (emittedEvents != null && emittedEvents.Length > 0) {
												actualEmittedEvents.AddRange(emittedEvents);
											}

											Assert.True(result);
										});
								}
							}

							yield return For(
								$"process event {@event.EventType} at {er.EventNumber}@{sequence.Stream}",
								() => {
									Assert.True(runner.ProcessEvent(expectedPartition, CheckpointTag.Empty, "", e,
										out var newState, out var newSharedState, out var emittedEvents), "Process event should always return true");
									var result = runner.TransformStateToResult();
									if (newSharedState != null) {
										partitionedState[""] = newSharedState;
									}

									partitionedState[expectedPartition] = newState;
									partitionedResult[expectedPartition] = result;

									if (emittedEvents != null && emittedEvents.Length > 0) {
										actualEmittedEvents.AddRange(emittedEvents);
									}


								});
							if (@event.ExpectedStates.Any()) {
								foreach (var expectedState in @event.ExpectedStates) {
									yield return For($@"state ""{expectedState.Key}"" exists at {er.EventNumber}@{sequence.Stream}",
										() => Assert.Contains(expectedState.Key, (IReadOnlyDictionary<string, string>)partitionedState));

									yield return For($@"state ""{expectedState.Key}"" is correct at {er.EventNumber}@{sequence.Stream}",
										() => {
											if (string.IsNullOrEmpty(expectedState.Value))  Assert.Null(partitionedState[expectedState.Key]);
											else {
												Assert.True(partitionedState.ContainsKey(expectedState.Key), $"partition does not contain key {expectedState.Key}");
												Assert.NotNull(partitionedState[expectedState.Key]);
												Assert.NotEmpty(partitionedState[expectedState.Key]);
												var expected = Sort(expectedState.Value, "expected");
												var actual = Sort(partitionedState[expectedState.Key], "actual");
												Assert.Equal(expected.ToString(Formatting.Indented), actual.ToString(Formatting.Indented));
											}
										});
								}
							}
							if (@event.ExpectedResult.Any()) {
								foreach (var expectedResult in @event.ExpectedResult) {

									yield return For($@"result ""{expectedResult.Key}"" exists at {er.EventNumber}@{sequence.Stream}",
										() => Assert.Contains(expectedResult.Key, (IReadOnlyDictionary<string, string>)partitionedResult));

									yield return For($@"result ""{expectedResult.Key}"" is correct at {er.EventNumber}@{sequence.Stream}",
										() => {
											if (string.IsNullOrEmpty(expectedResult.Value)) Assert.Null(partitionedResult[expectedResult.Key]);
											else {
												Assert.True(partitionedResult.ContainsKey(expectedResult.Key), $"partition does not contain key {expectedResult.Key}");
												if (expectedResult.Value is null) {
													Assert.Null(partitionedResult[expectedResult.Key]);
												} else {
													Assert.NotEmpty(partitionedResult[expectedResult.Key]);
													var expected = Sort(expectedResult.Value, "expected");
													var actual = Sort(partitionedResult[expectedResult.Key], "actual");
													Assert.Equal(expected.ToString(Formatting.Indented),
														actual.ToString(Formatting.Indented));
												}
											}
										});
								}
							}




						}
					}

					revision[sequence.Stream] = revision[sequence.Stream] + 1;
				}


			}

			if (expectedEmittedEvents.Count == 0) {
				yield return For("Should have no emitted events", () => {
					Assert.Empty(actualEmittedEvents);
				});
			} else {
				foreach (var emitted in expectedEmittedEvents) {
					yield return For(
						$"Expected event {emitted.StreamId} {emitted.Type} {(emitted.Type == "$>" ? emitted.Data : "")} was emitted",
						() => {
							Assert.Contains(actualEmittedEvents, eee => eee.Event.Data == emitted.Data);
						});
				}
			}

			object[] For(string name, Action a) {
				return new object[]{ new TestDefinition(Name(name),_ => {
					a();
					return default;
				})};
			}

			object[] WithOutput(string name, Action<ITestOutputHelper> a) {
				return new object[]{ new TestDefinition(Name(name),o => {
					a(o);
					return default;
				})};
			}

			string Name(string name) {
				if (name.StartsWith(projection!))
					return name;
				return $"{projection} {name}";
			}
		}

		static JObject Sort(string json, string name) {
			JObject root;
			try {
				root = JObject.Parse(json);
			} catch {
				throw;
			}
			return new(root.Properties().OrderBy(x => x.Name));
		}
	}

	[Theory]
	[MemberData(nameof(GetTestCases))]
	public Task Test(TestDefinition def) {
		return def.Execute(_output).AsTask();
	}

	public class TestDefinition {
		private readonly string _name;
		private readonly Func<ITestOutputHelper, ValueTask> _step;

		public TestDefinition(string name, Func<ITestOutputHelper, ValueTask> step) {
			_name = name;
			_step = step;
		}

		public ValueTask Execute(ITestOutputHelper output) {
			return _step(output);
		}

		public override string ToString() {
			return _name;
		}
	}

	class InputEventSequence {
		public string Stream { get; }
		private readonly List<InputEvent> _events;
		public List<InputEvent> Events => _events;

		public InputEventSequence(string stream) {
			Stream = stream;
			_events = new List<InputEvent>();
		}
	}

	class InputEvent(
		string eventType,
		string body,
		string metadata,
		IReadOnlyList<string> initializedPartitions,
		IReadOnlyDictionary<string, string> expectedStates,
		IReadOnlyDictionary<string, string> expectedResults,
		bool skip,
		Guid eventId) {

		public string EventType { get; } = eventType;
		public string Body { get; } = body;
		public string Metadata { get; } = metadata;
		public IReadOnlyList<string> InitializedPartitions { get; } = initializedPartitions;
		public IReadOnlyDictionary<string, string> ExpectedStates { get; } = expectedStates;
		public IReadOnlyDictionary<string, string> ExpectedResult { get; } = expectedResults;
		public bool Skip { get; } = skip;
		public Guid EventId { get; } = eventId;

	}

	class OutputEvent {
		public string StreamId { get; }
		public string Type { get; }
		public string Data { get; }
		public string Metadata { get; }

		public OutputEvent(string streamId, string type, string data, string metadata) {
			StreamId = streamId;
			Type = type;
			Data = data;
			Metadata = metadata;
		}
	}
}
