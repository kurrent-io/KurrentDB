// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Security.Claims;
using KurrentDB.Core.Data;
using KurrentDB.Core.Index.Hashes;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Tests.Fakes;
using KurrentDB.SecondaryIndexing.Indexes.Category;
using KurrentDB.SecondaryIndexing.Indexes.Default;
using KurrentDB.SecondaryIndexing.Indexes.Diagnostics;
using KurrentDB.SecondaryIndexing.Indexes.EventType;
using KurrentDB.SecondaryIndexing.Indexes.Stream;
using KurrentDB.SecondaryIndexing.Tests.Fakes;
using KurrentDB.SecondaryIndexing.Tests.Fixtures;
using static KurrentDB.SecondaryIndexing.Tests.Fakes.TestResolvedEventFactory;
using ReadStreamResult = KurrentDB.Core.Data.ReadStreamResult;

namespace KurrentDB.SecondaryIndexing.Tests.Indexes.DefaultIndexReaderTests;

public class DefaultIndexReaderTests : DuckDbIntegrationTest {
	[Fact]
	public async Task ReadForwards_WhenNonExistentStream_ReturnsNoStream() {
		// Given - no events indexed

		// When
		var result = await ReadForwards();

		// Then
		AssertEqual(
			ReadStreamEventsForwardCompleted(
				result: ReadStreamResult.NoStream,
				events: [],
				nextEventNumber: -1,
				lastEventNumber: -1,
				tfLastCommitPosition: 0, // TODO: sync dummy z processor
				isEndOfStream: true
			),
			result
		);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task ReadForwards_WhenValidationVersionMatches_ReturnsNotModified(bool shouldCommit) {
		// Given
		var events = new[] { From("test-stream", 0, 100, "TestEvent", []) };
		IndexEvents(events, shouldCommit);

		// When
		var result = await ReadForwards(validationStreamVersion: 0);

		// Then
		AssertEqual(
			ReadStreamEventsForwardCompleted(
				result: ReadStreamResult.NotModified,
				events: [],
				nextEventNumber: -1,
				tfLastCommitPosition: 100,
				lastEventNumber: 0L,
				isEndOfStream: true
			),
			result
		);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task ReadForwards_WhenFromEventNumberIsNegative_DefaultsToZero(bool shouldCommit) {
		// Given
		var events = new[] {
			From("test-stream", 0, 100, "TestEvent", []),
			From("test-stream", 1, 200, "TestEvent", [])
		};
		IndexEvents(events, shouldCommit);

		// When
		var result = await ReadForwards(fromEventNumber: -1, maxCount: 10);

		// Then
		AssertEqual(
			ReadStreamEventsForwardCompleted(
				result: ReadStreamResult.Success,
				events: events,
				fromEventNumber: -1,
				nextEventNumber: 2L,
				tfLastCommitPosition: 200,
				lastEventNumber: 1L,
				isEndOfStream: true
			),
			result
		);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task ReadForwards_WhenMaxCountOverflow_HandlesCorrectly(bool shouldCommit) {
		// Given
		var events = new[] { From("test-stream", 0, 100, "TestEvent", []) };
		IndexEvents(events, shouldCommit);

		// When
		var result = await ReadForwards(fromEventNumber: long.MaxValue - 5, maxCount: 10);

		// Then
		AssertEqual(
			ReadStreamEventsForwardCompleted(
				result: ReadStreamResult.Success,
				events: [],
				fromEventNumber: long.MaxValue - 5,
				maxCount: 10,
				nextEventNumber: -1,
				tfLastCommitPosition: 100,
				lastEventNumber: 0L,
				isEndOfStream: true
			),
			result
		);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task ReadForwards_WhenRequestBeyondExistingSet_ReturnsSuccessWithNoEvents(bool shouldCommit) {
		// Given
		var events = new[] { From("test-stream", 0, 100, "TestEvent", []) };
		IndexEvents(events, shouldCommit);

		// When
		var result = await ReadForwards(fromEventNumber: 5, maxCount: 10);

		// Then
		AssertEqual(
			ReadStreamEventsForwardCompleted(
				result: ReadStreamResult.Success,
				events: [],
				fromEventNumber: 5,
				maxCount: 10,
				nextEventNumber: -1,
				tfLastCommitPosition: 100,
				lastEventNumber: 0L,
				isEndOfStream: true
			),
			result
		);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task ReadForwards_WhenRequestBeyondExistingSetWithValidationVersion_UsesValidationVersion(bool shouldCommit) {
		// Given
		var events = new[] { From("test-stream", 0, 100, "TestEvent", []) };
		IndexEvents(events, shouldCommit);

		// When
		var result = await ReadForwards(fromEventNumber: 5, maxCount: 10, validationStreamVersion: 7);

		// Then
		AssertEqual(
			ReadStreamEventsForwardCompleted(
				result: ReadStreamResult.Success,
				events: [],
				fromEventNumber: 5,
				maxCount: 10,
				nextEventNumber: -1,
				tfLastCommitPosition: 100,
				lastEventNumber: 7L, // Should use ValidationStreamVersion
				isEndOfStream: true
			),
			result
		);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task ReadForwards_WhenSuccessfullyReadingWithMoreEventsAvailable_ReturnsCorrectResult(bool shouldCommit) {
		// Given
		var events = new[] {
			From("test-stream", 0, 100, "TestEvent", []),
			From("test-stream", 1, 200, "TestEvent", []),
			From("test-stream", 2, 300, "TestEvent", [])
		};
		IndexEvents(events, shouldCommit);

		// When
		var result = await ReadForwards(maxCount: 2);

		// Then
		AssertEqual(
			ReadStreamEventsForwardCompleted(
				result: ReadStreamResult.Success,
				events: events.Take(2).ToArray(),
				maxCount: 2,
				nextEventNumber: 2L,
				tfLastCommitPosition: 300,
				lastEventNumber: 2L,
				isEndOfStream: false
			),
			result
		);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task ReadForwards_WhenAtEndOfStream_ReturnsCorrectResult(bool shouldCommit) {
		// Given
		var events = new[] {
			From("test-stream", 0, 100, "TestEvent", []),
			From("test-stream", 1, 200, "TestEvent", [])
		};
		IndexEvents(events, shouldCommit);

		// When
		var result = await ReadForwards(fromEventNumber: 1, maxCount: 10);

		// Then
		AssertEqual(
			ReadStreamEventsForwardCompleted(
				result: ReadStreamResult.Success,
				events: [events[1]],
				fromEventNumber: 1,
				maxCount: 10,
				nextEventNumber: 2L,
				tfLastCommitPosition: 200,
				lastEventNumber: 1L,
				isEndOfStream: true
			),
			result
		);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task ReadForwards_WhenEventsExist_SetsNextEventNumberFromLastEvent(bool shouldCommit) {
		// Given
		var events = new[] {
			From("test-stream", 0, 100, "TestEvent", []),
			From("test-stream", 1, 200, "TestEvent", []),
			From("test-stream", 2, 300, "TestEvent", [])
		};
		IndexEvents(events, shouldCommit);

		// When
		var result = await ReadForwards(maxCount: 3);

		// Then
		AssertEqual(
			ReadStreamEventsForwardCompleted(
				result: ReadStreamResult.Success,
				events: events,
				maxCount: 3,
				nextEventNumber: 3L, // Last event number (2) + 1
				tfLastCommitPosition: 300,
				lastEventNumber: 2L,
				isEndOfStream: true
			),
			result
		);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task ReadBackwards_WhenValidationVersionMatches_ReturnsNotModified(bool shouldCommit) {
		// Given
		var events = new[] { From("test-stream", 0, 100, "TestEvent", []) };
		IndexEvents(events, shouldCommit);

		// When
		var result = await ReadBackwards(validationStreamVersion: 0);

		// Then
		AssertEqual(
			ReadStreamEventsBackwardCompleted(
				result: ReadStreamResult.NotModified,
				events: [],
				nextEventNumber: -1,
				tfLastCommitPosition: 100,
				lastEventNumber: 0L,
				isEndOfStream: true
			),
			result
		);
	}

	[Fact]
	public async Task ReadBackwards_WhenNonExistentStream_ReturnsNoStream() {
		// Given - no events indexed

		// When
		var result = await ReadBackwards();

		// Then
		AssertEqual(
			ReadStreamEventsBackwardCompleted(
				result: ReadStreamResult.NoStream,
				events: [],
				nextEventNumber: -1,
				lastEventNumber: -1,
				tfLastCommitPosition: 0,
				isEndOfStream: true
			),
			result
		);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task ReadBackwards_WhenNegativeFromEventNumber_UsesLastEventNumber(bool shouldCommit) {
		// Given
		var events = new[] {
			From("test-stream", 0, 100, "TestEvent", []),
			From("test-stream", 1, 200, "TestEvent", []),
			From("test-stream", 2, 300, "TestEvent", [])
		};
		IndexEvents(events, shouldCommit);

		// When
		var result = await ReadBackwards(fromEventNumber: -1, maxCount: 3);

		// Then
		AssertEqual(
			ReadStreamEventsBackwardCompleted(
				result: ReadStreamResult.Success,
				events: events.Reverse().ToArray(), // Ordered descending
				fromEventNumber: 2,
				maxCount: 3,
				nextEventNumber: -1,
				tfLastCommitPosition: 300,
				lastEventNumber: 2L,
				isEndOfStream: true
			),
			result
		);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task ReadBackwards_WhenEmptyResultSet_ReturnsSuccessWithNoEvents(bool shouldCommit) {
		// Given
		var events = new[] { From("test-stream", 0, 100, "TestEvent", []) };
		IndexEvents(events, shouldCommit);

		// When - request events beyond what exists
		var result = await ReadBackwards(fromEventNumber: 5, maxCount: 5);

		// Then
		AssertEqual(
			ReadStreamEventsBackwardCompleted(
				result: ReadStreamResult.Success,
				events: [],
				fromEventNumber: 5,
				maxCount: 5,
				nextEventNumber: -1,
				tfLastCommitPosition: 100,
				lastEventNumber: 0L,
				isEndOfStream: true
			),
			result
		);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task ReadBackwards_WhenEmptyResultSetWithValidationVersion_UsesValidationVersion(bool shouldCommit) {
		// Given
		var events = new[] { From("test-stream", 0, 100, "TestEvent", []) };
		IndexEvents(events, shouldCommit);

		// When
		var result = await ReadBackwards(fromEventNumber: 5, maxCount: 5, validationStreamVersion: 3);

		// Then
		AssertEqual(
			ReadStreamEventsBackwardCompleted(
				result: ReadStreamResult.Success,
				events: [],
				fromEventNumber: 5,
				maxCount: 5,
				nextEventNumber: -1,
				tfLastCommitPosition: 100,
				lastEventNumber: 3L, // Should use ValidationStreamVersion
				isEndOfStream: true
			),
			result
		);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task ReadBackwards_WhenAtBeginningOfStream_ReturnsIsEndOfStreamTrue(bool shouldCommit) {
		// Given
		var events = new[] {
			From("test-stream", 0, 100, "TestEvent", []),
			From("test-stream", 1, 200, "TestEvent", []),
			From("test-stream", 2, 300, "TestEvent", [])
		};
		IndexEvents(events, shouldCommit);

		// When
		var result = await ReadBackwards(fromEventNumber: 2, maxCount: 5);

		// Then
		AssertEqual(
			ReadStreamEventsBackwardCompleted(
				result: ReadStreamResult.Success,
				events: events.Reverse().ToArray(), // All events in descending order
				fromEventNumber: 2,
				maxCount: 5,
				nextEventNumber: -1,
				tfLastCommitPosition: 300,
				lastEventNumber: 2L,
				isEndOfStream: true
			),
			result
		);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task ReadBackwards_WhenMoreEventsAvailable_ReturnsCorrectNextEventNumber(bool shouldCommit) {
		// Given
		var events = new[] {
			From("test-stream", 0, 100, "TestEvent", []),
			From("test-stream", 1, 200, "TestEvent", []),
			From("test-stream", 2, 300, "TestEvent", []),
			From("test-stream", 3, 400, "TestEvent", []),
			From("test-stream", 4, 500, "TestEvent", [])
		};
		IndexEvents(events, shouldCommit);

		// When
		var result = await ReadBackwards(fromEventNumber: 4, maxCount: 2);

		// Then
		AssertEqual(
			ReadStreamEventsBackwardCompleted(
				result: ReadStreamResult.Success,
				events: new[] { events[4], events[3] }, // Last 2 events in descending order
				fromEventNumber: 4,
				maxCount: 2,
				nextEventNumber: 2L, // startEventNumber - 1 = 3 - 1 = 2
				tfLastCommitPosition: 500,
				lastEventNumber: 4L,
				isEndOfStream: false
			),
			result
		);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task ReadBackwards_WhenRecordsLengthIsZero_IsEndOfStreamTrue(bool shouldCommit) {
		// Given
		var events = new[] { From("test-stream", 0, 100, "TestEvent", []) };
		IndexEvents(events, shouldCommit);

		// When - request events that don't exist
		var result = await ReadBackwards(fromEventNumber: 5, maxCount: 2);

		// Then
		AssertEqual(
			ReadStreamEventsBackwardCompleted(
				result: ReadStreamResult.Success,
				events: [], // records.Length is 0
				fromEventNumber: 5,
				maxCount: 2,
				nextEventNumber: -1,
				tfLastCommitPosition: 100,
				lastEventNumber: 0L,
				isEndOfStream: true
			),
			result
		);
	}

	[Fact]
	public void GetLastEventNumber_WhenNoRecords_ReturnsLastSequence() {
		// Given -- no records
		var streamId = Guid.NewGuid().ToString(); // this can be anything for a Default Index, as it's ignored

		// When
		var result = _sut.GetLastEventNumber(streamId);

		// Then
		Assert.Equal(-1, result);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void GetLastEventNumber_WhenRecordsIndexed_ReturnsLastSequence(bool shouldCommit) {
		// Given
		var events = new[] {
			From("test-stream", 0, 100, "TestEvent", []),
			From("test-stream", 1, 200, "TestEvent", []),
			From("test-stream", 2, 300, "TestEvent", []),
			From("test-stream", 3, 400, "TestEvent", [])
		};
		IndexEvents(events, shouldCommit);

		// When
		var result = _sut.GetLastEventNumber("test-stream");

		// Then
		Assert.Equal(3L, result);
	}

	[Fact]
	public void CanReadStream_WhenStreamIsDefaultIndexName_ReturnsTrue() {
		// When
		var result = _sut.CanReadStream(DefaultIndex.Name);

		// Then
		Assert.True(result);
	}

	[Fact]
	public void CanReadStream_WhenStreamIsNotDefaultIndexName_ReturnsFalse() {
		// When
		var result = _sut.CanReadStream("other-stream");

		// Then
		Assert.False(result);
	}

	[Fact]
	public void GetLastIndexedPosition_WhenNoRecords_ReturnsProcessorLastIndexedPosition() {
		// Given -- no records
		var streamId = Guid.NewGuid().ToString(); // this can be anything for a Default Index, as it's ignored

		// When
		var result = _sut.GetLastIndexedPosition(streamId);

		// Then
		Assert.Equal(-1L, result);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void GetLastIndexedPosition_WhenIndexedRecords_ReturnsProcessorLastIndexedPosition(bool shouldCommit) {
		// Given
		var events = new[] {
			From("test-stream", 0, 100, "TestEvent", []),
			From("test-stream", 1, 200, "TestEvent", []),
			From("test-stream", 2, 300, "TestEvent", [])
		};
		IndexEvents(events, shouldCommit);
		var streamId = Guid.NewGuid().ToString(); // this can be anything for a Default Index, as it's ignored

		// When
		var result = _sut.GetLastIndexedPosition(streamId);

		// Then
		Assert.Equal(300L, result);
	}

	private void IndexEvents(ResolvedEvent[] events, bool shouldCommit) {
		_readIndexStub.IndexEvents(events);

		foreach (var resolvedEvent in events) {
			_processor.Index(resolvedEvent);
		}

		if (shouldCommit)
			_processor.Commit();
	}

	private async Task<ClientMessage.ReadStreamEventsForwardCompleted> ReadForwards(
		long fromEventNumber = 0,
		int maxCount = 10,
		bool resolveLinkTos = true,
		bool requireLeader = true,
		long? validationStreamVersion = null,
		ClaimsPrincipal? user = null,
		bool replyOnExpired = false,
		TimeSpan? longPollTimeout = null,
		DateTime? expires = null
	) {
		var tcs = new TaskCompletionSource<ClientMessage.ReadStreamEventsForwardCompleted>();
		var envelope = new CallbackEnvelope(m => {
			Assert.IsType<ClientMessage.ReadStreamEventsForwardCompleted>(m);
			tcs.SetResult((ClientMessage.ReadStreamEventsForwardCompleted)m);
		});

		var msg = new ClientMessage.ReadStreamEventsForward(
			_internalCorrId,
			_correlationId,
			envelope,
			DefaultIndex.Name,
			fromEventNumber,
			maxCount,
			resolveLinkTos,
			requireLeader,
			validationStreamVersion,
			user,
			replyOnExpired,
			longPollTimeout,
			expires,
			CancellationToken.None
		);

		var result = await _sut.ReadForwards(msg, CancellationToken.None);
		envelope.ReplyWith(result);

		return await tcs.Task;
	}

	private ClientMessage.ReadStreamEventsForwardCompleted ReadStreamEventsForwardCompleted(
		ReadStreamResult result,
		IReadOnlyList<ResolvedEvent> events,
		long fromEventNumber = 0,
		int maxCount = 10,
		long nextEventNumber = -1,
		long lastEventNumber = -1,
		bool isEndOfStream = false,
		long tfLastCommitPosition = -1,
		StreamMetadata? streamMetadata = null,
		bool isCachePublic = false,
		string error = ""
	) =>
		new(
			_correlationId,
			EventStreamId,
			fromEventNumber,
			maxCount,
			result,
			events,
			streamMetadata,
			isCachePublic,
			error,
			nextEventNumber,
			lastEventNumber,
			isEndOfStream,
			tfLastCommitPosition
		);

	private async Task<ClientMessage.ReadStreamEventsBackwardCompleted> ReadBackwards(
		long fromEventNumber = 10,
		int maxCount = 5,
		bool resolveLinkTos = true,
		bool requireLeader = true,
		long? validationStreamVersion = null,
		ClaimsPrincipal? user = null,
		DateTime? expires = null
	) {
		var tcs = new TaskCompletionSource<ClientMessage.ReadStreamEventsBackwardCompleted>();
		var envelope = new CallbackEnvelope(m => {
			Assert.IsType<ClientMessage.ReadStreamEventsBackwardCompleted>(m);
			tcs.SetResult((ClientMessage.ReadStreamEventsBackwardCompleted)m);
		});

		var msg = new ClientMessage.ReadStreamEventsBackward(
			_internalCorrId,
			_correlationId,
			envelope,
			DefaultIndex.Name,
			fromEventNumber,
			maxCount,
			resolveLinkTos,
			requireLeader,
			validationStreamVersion,
			user,
			expires,
			CancellationToken.None
		);

		var result = await _sut.ReadBackwards(msg, CancellationToken.None);
		envelope.ReplyWith(result);

		return await tcs.Task;
	}

	private ClientMessage.ReadStreamEventsBackwardCompleted ReadStreamEventsBackwardCompleted(
		ReadStreamResult result,
		IReadOnlyList<ResolvedEvent> events,
		long fromEventNumber = 10,
		int maxCount = 5,
		long nextEventNumber = -1,
		long lastEventNumber = -1,
		bool isEndOfStream = false,
		long tfLastCommitPosition = 0,
		StreamMetadata? streamMetadata = null,
		bool isCachePublic = false,
		string error = ""
	) =>
		new(
			_correlationId,
			EventStreamId,
			fromEventNumber,
			maxCount,
			result,
			events,
			streamMetadata,
			isCachePublic,
			error,
			nextEventNumber,
			lastEventNumber,
			isEndOfStream,
			tfLastCommitPosition
		);

	private static void AssertEqual(
		ClientMessage.ReadStreamEventsForwardCompleted expected,
		ClientMessage.ReadStreamEventsForwardCompleted actual
	) {
		Assert.Equal(expected.CorrelationId, actual.CorrelationId);
		Assert.Equal(expected.EventStreamId, actual.EventStreamId);
		Assert.Equal(expected.FromEventNumber, actual.FromEventNumber);
		Assert.Equal(expected.MaxCount, actual.MaxCount);
		Assert.Equal(expected.Result, actual.Result);
		Assert.Equivalent(expected.Events.Select(e => e.Event), actual.Events.Select(e => e.Event).ToList());
		// Assert.Equal(expected.StreamMetadata, actual.StreamMetadata);
		Assert.Equal(expected.IsCachePublic, actual.IsCachePublic);
		Assert.Equal(expected.Error, actual.Error);
		Assert.Equal(expected.NextEventNumber, actual.NextEventNumber);
		Assert.Equal(expected.LastEventNumber, actual.LastEventNumber);
		Assert.Equal(expected.IsEndOfStream, actual.IsEndOfStream);
		Assert.Equal(expected.TfLastCommitPosition, actual.TfLastCommitPosition);
	}

	private static void AssertEqual(
		ClientMessage.ReadStreamEventsBackwardCompleted expected,
		ClientMessage.ReadStreamEventsBackwardCompleted actual
	) {
		Assert.Equal(expected.CorrelationId, actual.CorrelationId);
		Assert.Equal(expected.EventStreamId, actual.EventStreamId);
		Assert.Equal(expected.FromEventNumber, actual.FromEventNumber);
		Assert.Equal(expected.MaxCount, actual.MaxCount);
		Assert.Equal(expected.Result, actual.Result);
		Assert.Equivalent(expected.Events.Select(e => e.Event), actual.Events.Select(e => e.Event).ToList());
		Assert.Equal(expected.IsCachePublic, actual.IsCachePublic);
		Assert.Equal(expected.Error, actual.Error);
		Assert.Equal(expected.NextEventNumber, actual.NextEventNumber);
		Assert.Equal(expected.LastEventNumber, actual.LastEventNumber);
		Assert.Equal(expected.IsEndOfStream, actual.IsEndOfStream);
		Assert.Equal(expected.TfLastCommitPosition, actual.TfLastCommitPosition);
	}

	private readonly DefaultIndexProcessor _processor;
	private readonly DefaultIndexReader _sut;
	private readonly Guid _internalCorrId = Guid.NewGuid();
	private readonly Guid _correlationId = Guid.NewGuid();
	private const string EventStreamId = DefaultIndex.Name;
	private readonly ReadIndexStub _readIndexStub = new();

	public DefaultIndexReaderTests() {
		const int commitBatchSize = 9;
		var hasher = new CompositeHasher<string>(new XXHashUnsafe(), new Murmur3AUnsafe());
		var inFlightRecords = new DefaultIndexInFlightRecords(
			new SecondaryIndexingPluginOptions { CommitBatchSize = commitBatchSize });

		var publisher = new FakePublisher();
		var categoryIndexProcessor = new CategoryIndexProcessor(DuckDb, publisher);
		var eventTypeIndexProcessor = new EventTypeIndexProcessor(DuckDb, publisher);
		var streamIndexProcessor =
			new StreamIndexProcessor(DuckDb, _readIndexStub.ReadIndex.IndexReader.Backend, hasher);

		_processor = new DefaultIndexProcessor(
			DuckDb,
			inFlightRecords,
			categoryIndexProcessor,
			eventTypeIndexProcessor,
			streamIndexProcessor,
			new NoOpSecondaryIndexProgressTracker(),
			publisher
		);

		_sut = new DefaultIndexReader(DuckDb, _processor, inFlightRecords, _readIndexStub.ReadIndex);
	}
}
