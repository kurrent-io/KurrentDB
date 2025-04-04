// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.Helpers;
using EventStore.Core.Messages;
using EventStore.Core.Services;
using EventStore.Core.Services.UserManagement;
using EventStore.Core.Tests.Fakes;
using EventStore.Core.Tests.Services.Replication;
using EventStore.Core.Util;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using EventStore.Projections.Core.Services.Processing.MultiStream;
using EventStore.Projections.Core.XUnit.Tests.TestHelpers;
using Xunit;
using ExistingEvent = EventStore.Projections.Core.XUnit.Tests.TestHelpers.ExistingStreamsHelper.ExistingEvent;

namespace EventStore.Projections.Core.XUnit.Tests.CheckpointManagers;

/// More tests for MultiStreamMultiOutputCheckpointManager exist in the NUnit tests,
/// and derive from <see cref="Core.Tests.Services.core_projection.checkpoint_manager.multi_stream.TestFixtureWithMultiStreamCheckpointManager"/>
public class MultiStreamMultiOutputCheckpointManagerTests {
	private const string ProjectionName = "test-projection";
	private const string OrderStreamName = $"$projections-{ProjectionName}-order";
	private const int ProjectionPhase = 0;
	private readonly MultiStreamMultiOutputCheckpointManager _sut;
	private readonly FakePublisher _publisher;
	private readonly IODispatcher _ioDispatcher;
	private readonly CoreProjectionCheckpointWriter _checkpointWriter;
	private readonly ExistingStreamsHelper _existingStreams;

	public MultiStreamMultiOutputCheckpointManagerTests() {
		var projectionId = Guid.NewGuid();
		var projectionVersion = new ProjectionVersion(3, ProjectionPhase, 5);
		var namingBuilder = new ProjectionNamesBuilder(ProjectionName, new ProjectionSourceDefinition());
		_publisher = new FakePublisher();
		var envelope = new FakeEnvelope();
		_ioDispatcher = new IODispatcher(_publisher, envelope);
		_checkpointWriter = new CoreProjectionCheckpointWriter(
			namingBuilder.MakeCheckpointStreamName(), _ioDispatcher, projectionVersion, ProjectionName);
		_existingStreams = new ExistingStreamsHelper();

		var projectionConfig = new ProjectionConfig(SystemAccounts.System, 10, 1000, 20, 2,
			true, true, false, false, false, 10000, 100, null);
		var positionTagger = new FakePositionTagger(ProjectionPhase);

		_sut = new MultiStreamMultiOutputCheckpointManager(
			_publisher, projectionId, projectionVersion, SystemAccounts.System, _ioDispatcher, projectionConfig, ProjectionName,
			positionTagger, namingBuilder, usePersistentCheckpoints: true, producesRunningResults: true, definesFold: false,
			_checkpointWriter, Opts.MaxProjectionStateSizeDefault);
	}

	[Theory]
	[MemberData(nameof(PreRecordedEvents))]
	public void when_loading_prerecorded_events(PreRecordedEventsScenario scenario) {
		_existingStreams.AddEvents(scenario.WithExistingEvents);
		_existingStreams.HardDeleteStreams(scenario.WithHardDeletedStreams);

		var checkpointTag = CheckpointTag.Empty;
		_checkpointWriter.StartFrom(checkpointTag, 0);
		_sut.BeginLoadPrerecordedEvents(checkpointTag);

		// Read the order stream to find prerecorded events
		var orderStreamRead = _publisher.Messages
			.OfType<ClientMessage.ReadStreamEventsBackward>()
			.FirstOrDefault();
		Assert.NotNull(orderStreamRead);
		Assert.Equal(OrderStreamName, orderStreamRead.EventStreamId);
		_ioDispatcher.BackwardReader.Handle(
			_existingStreams.ReadStreamBackward(orderStreamRead));

		// Read each event in the input streams 1 event at a time
		var readRequests = _publisher.Messages
			.OfType<ClientMessage.ReadStreamEventsBackward>()
			.Skip(1) // Order stream read
			.ToArray();
		for (var i = 0; i < scenario.ExpectedInputStreamReads.Length; i++) {
			var (expectedStream, expectedFrom) = scenario.ExpectedInputStreamReads[i];
			var actual = readRequests[i];
			Assert.Equal(expectedStream, actual.EventStreamId);
			Assert.Equal(expectedFrom, actual.FromEventNumber);
			Assert.Equal(1, actual.MaxCount);
			_ioDispatcher.BackwardReader.Handle(_existingStreams.ReadStreamBackward(actual));
		}

		// Publish a CommittedEventReceived for the prerecorded events that were found
		var committedEvents = _publisher.Messages
			.OfType<EventReaderSubscriptionMessage.CommittedEventReceived>().ToArray();
		Assert.Equal(scenario.ExpectedCommittedEvents.Length, committedEvents.Length);
		for (var i = 0; i < scenario.ExpectedCommittedEvents.Length; i++) {
			var exp = scenario.ExpectedCommittedEvents[i];
			var act = committedEvents[i];
			Assert.Equal(exp.EventStreamId, act.Data.EventStreamId);
			Assert.Equal(exp.EventNumber, act.Data.EventSequenceNumber);
			Assert.Equal(i, act.SubscriptionMessageSequenceNumber);
			Assert.Equal(exp.PreparePosition, act.Data.Position.PreparePosition);
		}

		// Publish completed message
		var completeMessage = _publisher.Messages
			.OfType<CoreProjectionProcessingMessage.PrerecordedEventsLoaded>()
			.SingleOrDefault();
		Assert.NotNull(completeMessage);
	}

	public readonly struct PreRecordedEventsScenario(
		string scenarioName,
		ExistingEvent[] withExistingEvents,
		ExistingEvent[] expectedCommittedEvents,
		Tuple<string, long>[] expectedInputStreamReads,
		string[]? withHardDeletedStreams = null) {
		private string ScenarioName { get; } = scenarioName;
		public ExistingEvent[] WithExistingEvents { get; } = withExistingEvents;
		public ExistingEvent[] ExpectedCommittedEvents { get; } = expectedCommittedEvents;
		public Tuple<string, long>[] ExpectedInputStreamReads { get; } = expectedInputStreamReads;
		public string[] WithHardDeletedStreams { get; } = withHardDeletedStreams ?? [];

		public override string ToString() => ScenarioName;
	}

	public static TheoryData<PreRecordedEventsScenario> PreRecordedEvents() =>
		new() {
			new PreRecordedEventsScenario("No prerecorded events", [], [], []),
			new PreRecordedEventsScenario("All prerecorded events can be read",
				withExistingEvents: new ExistingEvent[] {
					new("a", 0, 100, """{ "data": "a0" }"""),
					new(OrderStreamName, 0, 200, "0@a", """{"s": { "a": 0, "b": 0 }}""", SystemEventTypes.LinkTo),
					new("b", 0, 300, """{ "data": "b0" }"""),
					new(OrderStreamName, 1, 400, "0@b", """{"s": { "a": 0, "b": 0 }}""", SystemEventTypes.LinkTo),
					new("a", 1, 500, """{ "data": "a1" }"""),
					new(OrderStreamName, 2, 600, "1@a", """{"s": { "a": 1, "b": 0 }}""", SystemEventTypes.LinkTo),
					new("b", 1, 700, """{ "data": "b1" }"""),
					new(OrderStreamName, 3, 800, "1@b", """{"s": { "a": 1, "b": 1 }}""", SystemEventTypes.LinkTo),
				},
				expectedCommittedEvents: new ExistingEvent[] {
					new("a", 0, 100, """{ "data": "a0" }"""),
					new("b", 0, 300, """{ "data": "b0" }"""),
					new("a", 1, 500, """{ "data": "a1" }"""),
					new("b", 1, 700, """{ "data": "b1" }"""),
				},
				expectedInputStreamReads: new Tuple<string, long>[] {
					new("b", 1),
					new("a", 1),
					new("b", 0),
					new("a", 0),
				}
			),
			new PreRecordedEventsScenario("Some prerecorded events have been truncated",
				withExistingEvents: new ExistingEvent[] {
					new("a", 0, 100, """{ "data": "a0" }""", ""),
					new(OrderStreamName, 0, 200, "0@a", """{"s": { "a": 0, "b": 0 }}""", SystemEventTypes.LinkTo),
					// b@0 has been deleted
					new(OrderStreamName, 1, 400, "0@b", """{"s": { "a": 0, "b": 0 }}""", SystemEventTypes.LinkTo),
					new("a", 1, 500, """{ "data": "a1" }"""),
					new(OrderStreamName, 2, 600, "1@a", """{"s": { "a": 1, "b": 0 }}""", SystemEventTypes.LinkTo),
					// b@1 has been deleted
					new(OrderStreamName, 3, 800, "1@b", """{"s": { "a": 1, "b": 1 }}""", SystemEventTypes.LinkTo),
					new("a", 2, 900, """{ "data": "a2" }"""),
					new(OrderStreamName, 4, 1000, "2@a", """{"s": { "a": 2, "b": 1 }}""", SystemEventTypes.LinkTo),
					new("b", 2, 1100, """{ "data": "b2" }"""),
					new(OrderStreamName, 5, 1200, "2@b", """{"s": { "a": 2, "b": 2 }}""", SystemEventTypes.LinkTo)
				},
				expectedCommittedEvents: new ExistingEvent[] {
					new("a", 0, 100, """{ "data": "a0" }"""),
					new("a", 1, 500, """{ "data": "a1" }"""),
					new("a", 2, 900, """{ "data": "a2" }"""),
					new("b", 2, 1100, """{ "data": "b2" }"""),
				},
				expectedInputStreamReads: new Tuple<string, long>[] {
					new("b", 2),
					new("a", 2),
					new("b", 1),
					new("a", 1),
					new("b", 0),
					new("a", 0),
				}),
			new PreRecordedEventsScenario("All prerecorded events have been truncated",
				withExistingEvents: new ExistingEvent[] {
					new(OrderStreamName, 0, 200, "0@a", """{"s": { "a": 0, "b": 0 }}""", SystemEventTypes.LinkTo),
					new(OrderStreamName, 1, 400, "0@b", """{"s": { "a": 0, "b": 0 }}""", SystemEventTypes.LinkTo),
					new(OrderStreamName, 2, 600, "1@a", """{"s": { "a": 1, "b": 0 }}""", SystemEventTypes.LinkTo),
					new(OrderStreamName, 3, 800, "1@b", """{"s": { "a": 1, "b": 1 }}""", SystemEventTypes.LinkTo),
					new(OrderStreamName, 4, 1000, "2@a", """{"s": { "a": 2, "b": 1 }}""", SystemEventTypes.LinkTo),
					new(OrderStreamName, 5, 1200, "2@b", """{"s": { "a": 2, "b": 2 }}""", SystemEventTypes.LinkTo),
					// We must write events at the end of the input stream because this is a different scenario to stream not found.
					new("a", 3, 1300, """{ "data": "a3" }"""),
					new("b", 3, 1400, """{ "data": "b3" }"""),
				},
				expectedCommittedEvents: new ExistingEvent[] { },
				expectedInputStreamReads: new Tuple<string, long>[] {
					new("b", 2),
					new("a", 2),
					new("b", 1),
					new("a", 1),
					new("b", 0),
					new("a", 0),
				}),
			new PreRecordedEventsScenario("All input streams have been soft deleted",
				withExistingEvents: new ExistingEvent[] {
					new(OrderStreamName, 0, 200, "0@a", """{"s": { "a": 0, "b": 0 }}""", SystemEventTypes.LinkTo),
					new(OrderStreamName, 1, 400, "0@b", """{"s": { "a": 0, "b": 0 }}""", SystemEventTypes.LinkTo),
					new(OrderStreamName, 2, 600, "1@a", """{"s": { "a": 1, "b": 0 }}""", SystemEventTypes.LinkTo),
					new(OrderStreamName, 3, 800, "1@b", """{"s": { "a": 1, "b": 1 }}""", SystemEventTypes.LinkTo),
				},
				expectedCommittedEvents: new ExistingEvent[] { },
				expectedInputStreamReads: new Tuple<string, long>[] {
					new("b", 1),
					new("a", 1),
					new("b", 0),
					new("a", 0),
				}),
			new PreRecordedEventsScenario("One of the input streams has been hard deleted",
				withExistingEvents: new ExistingEvent[] {
					new("a", 0, 100, """{ "data": "a0" }"""),
					new(OrderStreamName, 0, 200, "0@a", """{"s": { "a": 0, "b": 0 }}""", SystemEventTypes.LinkTo),
					new(OrderStreamName, 1, 400, "0@b", """{"s": { "a": 0, "b": 0 }}""", SystemEventTypes.LinkTo),
					new("a", 1, 500, """{ "data": "a1" }"""),
					new(OrderStreamName, 2, 600, "1@a", """{"s": { "a": 1, "b": 0 }}""", SystemEventTypes.LinkTo),
					new(OrderStreamName, 3, 800, "1@b", """{"s": { "a": 1, "b": 1 }}""", SystemEventTypes.LinkTo),
				},
				withHardDeletedStreams: new string[] { "b" },
				expectedCommittedEvents: new ExistingEvent[] {
					new("a", 0, 100, """{ "data": "a0" }"""),
					new("a", 1, 500, """{ "data": "a1" }"""),
				},
				expectedInputStreamReads: new Tuple<string, long>[] {
					new("b", 1),
					new("a", 1),
					new("b", 0),
					new("a", 0),
				})
		};
}
