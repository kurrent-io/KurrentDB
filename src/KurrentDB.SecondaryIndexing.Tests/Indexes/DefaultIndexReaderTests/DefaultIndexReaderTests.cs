// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Security.Claims;
using KurrentDB.Core.Data;
using KurrentDB.Core.DataStructures;
using KurrentDB.Core.Index.Hashes;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Core.Tests.Fakes;
using KurrentDB.Core.TransactionLog;
using KurrentDB.Core.TransactionLog.LogRecords;
using KurrentDB.SecondaryIndexing.Indexes.Category;
using KurrentDB.SecondaryIndexing.Indexes.Default;
using KurrentDB.SecondaryIndexing.Indexes.Diagnostics;
using KurrentDB.SecondaryIndexing.Indexes.EventType;
using KurrentDB.SecondaryIndexing.Indexes.Stream;
using KurrentDB.SecondaryIndexing.Tests.Fixtures;
using NSubstitute;
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

	[Fact]
	public async Task ReadForwards_WhenValidationVersionMatches_ReturnsNotModified() {
		// Given
		var events = new[] { From("test-stream", 0, 100, "TestEvent", []) };
		IndexEvents(events);

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

	[Fact]
	public async Task ReadForwards_WhenFromEventNumberIsNegative_DefaultsToZero() {
		// Given
		var events = new[] {
			From("test-stream", 0, 100, "TestEvent", []),
			From("test-stream", 1, 200, "TestEvent", [])
		};
		IndexEvents(events);

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



	[Fact]
	public async Task ReadForwards_WhenMaxCountOverflow_HandlesCorrectly()
	{
		// Given
		var events = new[] { From("test-stream", 0, 100, "TestEvent", []) };
		IndexEvents(events);

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

    [Fact]
    public async Task ReadForwards_WhenRequestBeyondExistingSet_ReturnsSuccessWithNoEvents()
    {
        // Given
        var events = new[] { From("test-stream", 0, 100, "TestEvent", []) };
        IndexEvents(events);

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

    [Fact]
    public async Task ReadForwards_WhenRequestBeyondExistingSetWithValidationVersion_UsesValidationVersion()
    {
        // Given
        var events = new[] { From("test-stream", 0, 100, "TestEvent", []) };
        IndexEvents(events);

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

    [Fact]
    public async Task ReadForwards_WhenSuccessfullyReadingWithMoreEventsAvailable_ReturnsCorrectResult()
    {
        // Given
        var events = new[] {
            From("test-stream", 0, 100, "TestEvent", []),
            From("test-stream", 1, 200, "TestEvent", []),
            From("test-stream", 2, 300, "TestEvent", [])
        };
        IndexEvents(events);

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

    [Fact]
    public async Task ReadForwards_WhenAtEndOfStream_ReturnsCorrectResult()
    {
        // Given
        var events = new[] {
            From("test-stream", 0, 100, "TestEvent", []),
            From("test-stream", 1, 200, "TestEvent", [])
        };
        IndexEvents(events);

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

    [Fact]
    public async Task ReadForwards_WhenEventsExist_SetsNextEventNumberFromLastEvent()
    {
        // Given
        var events = new[] {
            From("test-stream", 0, 100, "TestEvent", []),
            From("test-stream", 1, 200, "TestEvent", []),
            From("test-stream", 2, 300, "TestEvent", [])
        };
        IndexEvents(events);

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

	private void IndexEvents(ResolvedEvent[] events) {
		foreach (var resolvedEvent in events) {
			_processor.Index(resolvedEvent);
		}

		_processor.Commit();

		_transactionalFileReader.TryReadAt(default, default, default).ReturnsForAnyArgs(x => {
			var logPosition = x.ArgAt<long>(0);

			if (!events.Any(e => e.Event.LogPosition == logPosition))
				return new RecordReadResult();

			var evnt = events.Single(e => e.Event.LogPosition == logPosition).Event;

			var prepare = new PrepareLogRecord(
				logPosition,
				evnt.CorrelationId,
				evnt.EventId,
				evnt.TransactionPosition,
				evnt.TransactionOffset,
				evnt.EventStreamId,
				null,
				evnt.ExpectedVersion,
				evnt.TimeStamp,
				evnt.Flags,
				evnt.EventType,
				null,
				evnt.Data,
				evnt.Metadata,
				evnt.Properties
			);

			return new RecordReadResult(true, -1, prepare, evnt.Data.Length);
		});

		_readIndex.LastIndexedPosition.Returns(events.Last().Event.LogPosition);
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
		// Then.Equal(expected.StreamMetadata, actual.StreamMetadata);
		Assert.Equal(expected.IsCachePublic, actual.IsCachePublic);
		Assert.Equal(expected.Error, actual.Error);
		Assert.Equal(expected.NextEventNumber, actual.NextEventNumber);
		Assert.Equal(expected.LastEventNumber, actual.LastEventNumber);
		Assert.Equal(expected.IsEndOfStream, actual.IsEndOfStream);
		Assert.Equal(expected.TfLastCommitPosition, actual.TfLastCommitPosition);
	}

	private readonly DefaultIndexProcessor _processor;
	private readonly DefaultIndexReader _sut;
	private readonly IReadIndex<string> _readIndex;
	private readonly Guid _internalCorrId = Guid.NewGuid();
	private readonly Guid _correlationId = Guid.NewGuid();
	private const string EventStreamId = DefaultIndex.Name;
	private readonly ITransactionFileReader _transactionalFileReader;

	public DefaultIndexReaderTests() {
		_transactionalFileReader = Substitute.For<ITransactionFileReader>();

		var lease = new TFReaderLease(
			new ObjectPool<ITransactionFileReader>("dummy", 1, 1, () => _transactionalFileReader));

		var backend = Substitute.For<IIndexBackend<string>>();

		var indexReader = Substitute.For<IIndexReader<string>>();
		indexReader.Backend.Returns(backend);
		indexReader.BorrowReader().Returns(lease);

		_readIndex = Substitute.For<IReadIndex<string>>();
		_readIndex.IndexReader.Returns(indexReader);

		const int commitBatchSize = 9;
		var hasher = new CompositeHasher<string>(new XXHashUnsafe(), new Murmur3AUnsafe());
		var inFlightRecords = new DefaultIndexInFlightRecords(
			new SecondaryIndexingPluginOptions { CommitBatchSize = commitBatchSize });

		var publisher = new FakePublisher();
		var categoryIndexProcessor = new CategoryIndexProcessor(DuckDb, publisher);
		var eventTypeIndexProcessor = new EventTypeIndexProcessor(DuckDb, publisher);
		var streamIndexProcessor = new StreamIndexProcessor(DuckDb, _readIndex.IndexReader.Backend, hasher);

		_processor = new DefaultIndexProcessor(
			DuckDb,
			inFlightRecords,
			categoryIndexProcessor,
			eventTypeIndexProcessor,
			streamIndexProcessor,
			new NoOpSecondaryIndexProgressTracker(),
			publisher
		);

		_sut = new DefaultIndexReader(DuckDb, _processor, inFlightRecords, _readIndex);
	}
}
