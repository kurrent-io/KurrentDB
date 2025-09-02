// // Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// // Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).
//
// using System.Security.Claims;
// using KurrentDB.Core.Data;
// using KurrentDB.Core.Messaging;
// using KurrentDB.SecondaryIndexing.Indexes.Default;
// using static KurrentDB.Core.Messages.ClientMessage;
// using static KurrentDB.SecondaryIndexing.Tests.Fakes.TestResolvedEventFactory;
// using ReadStreamResult = KurrentDB.Core.Data.ReadStreamResult;
//
// namespace KurrentDB.SecondaryIndexing.Tests.Indexes.DefaultIndexReaderTests;
//
// public class ReadBackwardsTests : IndexTestBase {
// 	[Fact]
// 	public async Task WhenNonExistentStream_ReturnsNoStream() {
// 		// Given - no events indexed
//
// 		// When
// 		var result = await ReadBackwards();
//
// 		// Then
// 		AssertEqual(
// 			ReadStreamEventsBackwardCompleted(
// 				result: ReadStreamResult.NoStream,
// 				events: [],
// 				nextEventNumber: -1,
// 				lastEventNumber: -1,
// 				tfLastCommitPosition: 0,
// 				isEndOfStream: true
// 			),
// 			result
// 		);
// 	}
//
// 	[Theory]
// 	[InlineData(true)]
// 	[InlineData(false)]
// 	public async Task WhenNegativeFromEventNumber_UsesLastEventNumber(bool shouldCommit) {
// 		// Given
// 		var events = new[] {
// 			From("test-stream", 0, 100, "TestEvent", []),
// 			From("test-stream", 1, 200, "TestEvent", []),
// 			From("test-stream", 2, 300, "TestEvent", [])
// 		};
// 		IndexEvents(events, shouldCommit);
//
// 		// When
// 		var result = await ReadBackwards(fromEventNumber: -1, maxCount: 3);
//
// 		// Then
// 		AssertEqual(
// 			ReadStreamEventsBackwardCompleted(
// 				result: ReadStreamResult.Success,
// 				events: events.Reverse().ToArray(), // Ordered descending
// 				fromEventNumber: 2,
// 				maxCount: 3,
// 				nextEventNumber: -1,
// 				tfLastCommitPosition: 300,
// 				lastEventNumber: 2L,
// 				isEndOfStream: true
// 			),
// 			result
// 		);
// 	}
//
// 	[Theory]
// 	[InlineData(true)]
// 	[InlineData(false)]
// 	public async Task WhenEmptyResultSet_ReturnsSuccessWithNoEvents(bool shouldCommit) {
// 		// Given
// 		var events = new[] { From("test-stream", 0, 100, "TestEvent", []) };
// 		IndexEvents(events, shouldCommit);
//
// 		// When - request events beyond what exists
// 		var result = await ReadBackwards(fromEventNumber: 5, maxCount: 5);
//
// 		// Then
// 		AssertEqual(
// 			ReadStreamEventsBackwardCompleted(
// 				result: ReadStreamResult.Success,
// 				events: [],
// 				fromEventNumber: 0,
// 				maxCount: 5,
// 				nextEventNumber: -1,
// 				tfLastCommitPosition: 100,
// 				lastEventNumber: 0L,
// 				isEndOfStream: true
// 			),
// 			result
// 		);
// 	}
//
// 	[Theory]
// 	[InlineData(true)]
// 	[InlineData(false)]
// 	public async Task WhenEmptyResultSetWithValidationVersion_UsesValidationVersion(bool shouldCommit) {
// 		// Given
// 		var events = new[] { From("test-stream", 0, 100, "TestEvent", []) };
// 		IndexEvents(events, shouldCommit);
//
// 		// When
// 		var result = await ReadBackwards(fromEventNumber: 3, maxCount: 5, validationStreamVersion: 3);
//
// 		// Then
// 		AssertEqual(
// 			ReadStreamEventsBackwardCompleted(
// 				result: ReadStreamResult.Success,
// 				events: [],
// 				fromEventNumber: 0,
// 				maxCount: 5,
// 				nextEventNumber: -1,
// 				tfLastCommitPosition: 100,
// 				lastEventNumber: 0,
// 				isEndOfStream: true
// 			),
// 			result
// 		);
// 	}
//
// 	[Theory]
// 	[InlineData(true)]
// 	[InlineData(false)]
// 	public async Task WhenEmptyNegativeFromEventNumber_UsesValidationVersion(bool shouldCommit) {
// 		// Given
// 		var events = new[] { From("test-stream", 0, 100, "TestEvent", []) };
// 		IndexEvents(events, shouldCommit);
//
// 		// When
// 		var result = await ReadBackwards(fromEventNumber: -1, maxCount: 5, validationStreamVersion: 3);
//
// 		// Then
// 		AssertEqual(
// 			ReadStreamEventsBackwardCompleted(
// 				result: ReadStreamResult.Success,
// 				events: [],
// 				fromEventNumber: 0,
// 				maxCount: 5,
// 				nextEventNumber: -1,
// 				tfLastCommitPosition: 100,
// 				lastEventNumber: 0,
// 				isEndOfStream: true
// 			),
// 			result
// 		);
// 	}
//
// 	[Theory]
// 	[InlineData(true)]
// 	[InlineData(false)]
// 	public async Task WhenAtBeginningOfStream_ReturnsIsEndOfStreamTrue(bool shouldCommit) {
// 		// Given
// 		var events = new[] {
// 			From("test-stream", 0, 100, "TestEvent", []),
// 			From("test-stream", 1, 200, "TestEvent", []),
// 			From("test-stream", 2, 300, "TestEvent", [])
// 		};
// 		IndexEvents(events, shouldCommit);
//
// 		// When
// 		var result = await ReadBackwards(fromEventNumber: 2, maxCount: 5);
//
// 		// Then
// 		AssertEqual(
// 			ReadStreamEventsBackwardCompleted(
// 				result: ReadStreamResult.Success,
// 				events: events.Reverse().ToArray(), // All events in descending order
// 				fromEventNumber: 2,
// 				maxCount: 5,
// 				nextEventNumber: -1,
// 				tfLastCommitPosition: 300,
// 				lastEventNumber: 2L,
// 				isEndOfStream: true
// 			),
// 			result
// 		);
// 	}
//
// 	[Theory]
// 	[InlineData(true)]
// 	[InlineData(false)]
// 	public async Task WhenValidationVersionMatches_ReturnsNotModified(bool shouldCommit) {
// 		// Given
// 		var events = new[] { From("test-stream", 0, 100, "TestEvent", []) };
// 		IndexEvents(events, shouldCommit);
//
// 		// When
// 		var result = await ReadBackwards(validationStreamVersion: 0);
//
// 		// Then
// 		AssertEqual(
// 			ReadStreamEventsBackwardCompleted(
// 				result: ReadStreamResult.NotModified,
// 				events: [],
// 				nextEventNumber: -1,
// 				tfLastCommitPosition: 100,
// 				lastEventNumber: 0L,
// 				isEndOfStream: true
// 			),
// 			result
// 		);
// 	}
//
// 	[Theory]
// 	[InlineData(true)]
// 	[InlineData(false)]
// 	public async Task WhenRequestingMoreEventsThanInTheStream(bool shouldCommit) {
// 		// Given
// 		var events = new[] {
// 			From("test-stream", 0, 100, "TestEvent", []),
// 			From("test-stream", 1, 200, "TestEvent", []),
// 			From("test-stream", 2, 300, "TestEvent", [])
// 		};
// 		IndexEvents(events, shouldCommit);
//
// 		// When - request events that don't exist
// 		var result = await ReadBackwards(fromEventNumber: long.MaxValue, maxCount: 1000);
//
// 		// Then
// 		AssertEqual(
// 			ReadStreamEventsBackwardCompleted(
// 				result: ReadStreamResult.Success,
// 				events: [],
// 				fromEventNumber: 2,
// 				maxCount: 1000,
// 				nextEventNumber: -1,
// 				tfLastCommitPosition: 300,
// 				lastEventNumber: 2L,
// 				isEndOfStream: true
// 			),
// 			result
// 		);
// 	}
//
// 	[Theory]
// 	[InlineData(true)]
// 	[InlineData(false)]
// 	public async Task WhenMoreEventsAvailable_ReturnsCorrectNextEventNumber(bool shouldCommit) {
// 		// Given
// 		var events = new[] {
// 			From("test-stream", 0, 100, "TestEvent", []),
// 			From("test-stream", 1, 200, "TestEvent", []),
// 			From("test-stream", 2, 300, "TestEvent", []),
// 			From("test-stream", 3, 400, "TestEvent", []),
// 			From("test-stream", 4, 500, "TestEvent", [])
// 		};
// 		IndexEvents(events, shouldCommit);
//
// 		// When
// 		var result = await ReadBackwards(fromEventNumber: 4, maxCount: 2);
//
// 		// Then
// 		AssertEqual(
// 			ReadStreamEventsBackwardCompleted(
// 				result: ReadResult.Success,
// 				events: [events[4], events[3]], // Last 2 events in descending order
// 				fromEventNumber: 4,
// 				maxCount: 2,
// 				nextEventNumber: 2L, // startEventNumber - 1 = 3 - 1 = 2
// 				tfLastCommitPosition: 500,
// 				lastEventNumber: 4L,
// 				isEndOfStream: false
// 			),
// 			result
// 		);
// 	}
//
// 	async Task<ReadIndexEventsBackwardCompleted> ReadBackwards(
// 		long fromEventNumber = 10,
// 		int maxCount = 5,
// 		bool resolveLinkTos = true,
// 		bool requireLeader = true,
// 		long? validationStreamVersion = null,
// 		ClaimsPrincipal? user = null,
// 		DateTime? expires = null
// 	) {
// 		var tcs = new TaskCompletionSource<ReadIndxEventsBackwardCompleted>();
// 		var envelope = new CallbackEnvelope(m => {
// 			Assert.IsType<ReadIndexEventsBackwardCompleted>(m);
// 			tcs.SetResult((ReadIndexEventsBackwardCompleted)m);
// 		});
//
// 		var msg = new ReadIndexEventsBackward(
// 			_internalCorrId,
// 			_correlationId,
// 			envelope,
// 			DefaultIndex.Name,
// 			fromEventNumber,
// 			maxCount,
// 			resolveLinkTos,
// 			requireLeader,
// 			validationStreamVersion,
// 			user,
// 			expires,
// 			CancellationToken.None
// 		);
//
// 		var result = await Sut.ReadBackwards(msg, CancellationToken.None);
// 		envelope.ReplyWith(result);
//
// 		return await tcs.Task;
// 	}
//
// 	private ReadIndexEventsBackwardCompleted ReadStreamEventsBackwardCompleted(
// 		ReadStreamResult result,
// 		IReadOnlyList<ResolvedEvent> events,
// 		long fromEventNumber = 10,
// 		int maxCount = 5,
// 		long nextEventNumber = -1,
// 		long lastEventNumber = -1,
// 		bool isEndOfStream = false,
// 		long tfLastCommitPosition = 0,
// 		StreamMetadata? streamMetadata = null,
// 		bool isCachePublic = false,
// 		string error = ""
// 	) =>
// 		new(
// 			_correlationId,
// 			EventStreamId,
// 			fromEventNumber,
// 			maxCount,
// 			result,
// 			events,
// 			streamMetadata,
// 			isCachePublic,
// 			error,
// 			nextEventNumber,
// 			lastEventNumber,
// 			isEndOfStream,
// 			tfLastCommitPosition
// 		);
//
// 	static void AssertEqual(
// 		ReadStreamEventsBackwardCompleted expected,
// 		ReadStreamEventsBackwardCompleted actual
// 	) {
// 		Assert.Equal(expected.CorrelationId, actual.CorrelationId);
// 		Assert.Equal(expected.EventStreamId, actual.EventStreamId);
// 		Assert.Equal(expected.FromEventNumber, actual.FromEventNumber);
// 		Assert.Equal(expected.MaxCount, actual.MaxCount);
// 		Assert.Equal(expected.Result, actual.Result);
// 		Assert.Equivalent(expected.Events.Select(e => e.Event), actual.Events.Select(e => e.Event).ToList());
// 		Assert.Equal(expected.IsCachePublic, actual.IsCachePublic);
// 		Assert.Equal(expected.Error, actual.Error);
// 		Assert.Equal(expected.NextEventNumber, actual.NextEventNumber);
// 		Assert.Equal(expected.LastEventNumber, actual.LastEventNumber);
// 		Assert.Equal(expected.IsEndOfStream, actual.IsEndOfStream);
// 		Assert.Equal(expected.TfLastCommitPosition, actual.TfLastCommitPosition);
// 	}
// }
//
