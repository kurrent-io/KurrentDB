// // Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// // Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).
//
// using System.Security.Claims;
// using KurrentDB.Core.Data;
// using KurrentDB.Core.Messages;
// using KurrentDB.Core.Messaging;
// using KurrentDB.SecondaryIndexing.Indexes.Default;
// using static KurrentDB.SecondaryIndexing.Tests.Fakes.TestResolvedEventFactory;
// using ReadStreamResult = KurrentDB.Core.Data.ReadStreamResult;
//
// namespace KurrentDB.SecondaryIndexing.Tests.Indexes.DefaultIndexReaderTests;
//
// public class ReadForwardsTests : IndexTestBase {
// 	[Fact]
// 	public async Task WhenNonExistentStream_ReturnsNoStream() {
// 		// Given - no events indexed
//
// 		// When
// 		var result = await ReadForwards();
//
// 		// Then
// 		AssertEqual(
// 			ReadForwardCompleted(
// 				result: ReadIndexResult.IndexNotFound,
// 				events: [],
// 				nextEventNumber: -1,
// 				lastEventNumber: -1,
// 				tfLastCommitPosition: 0, // TODO: sync dummy z processor
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
// 		var result = await ReadForwards(validationStreamVersion: 0);
//
// 		// Then
// 		AssertEqual(
// 			ReadForwardCompleted(
// 				result: ReadIndexResult.NotModified,
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
// 	public async Task WhenMaxCountOverflow_HandlesCorrectly(bool shouldCommit) {
// 		// Given
// 		var events = new[] { From("test-stream", 0, 100, "TestEvent", []) };
// 		IndexEvents(events, shouldCommit);
//
// 		// When
// 		var result = await ReadForwards(fromEventNumber: long.MaxValue - 5, maxCount: 10);
//
// 		// Then
// 		AssertEqual(
// 			ReadForwardCompleted(
// 				result: ReadIndexResult.Success,
// 				events: [],
// 				fromEventNumber: long.MaxValue - 5,
// 				maxCount: 10,
// 				nextEventNumber: 1,
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
// 	public async Task WhenRequestBeyondExistingSet_ReturnsSuccessWithNoEvents(bool shouldCommit) {
// 		// Given
// 		var events = new[] { From("test-stream", 0, 100, "TestEvent", []) };
// 		IndexEvents(events, shouldCommit);
//
// 		// When
// 		var result = await ReadForwards(fromEventNumber: 5, maxCount: 10);
//
// 		// Then
// 		AssertEqual(
// 			ReadForwardCompleted(
// 				result: ReadIndexResult.Success,
// 				events: [],
// 				fromEventNumber: 5,
// 				maxCount: 10,
// 				nextEventNumber: 1,
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
// 	public async Task WhenRequestBeyondExistingSetWithValidationVersion_UsesValidationVersion(bool shouldCommit) {
// 		// Given
// 		var events = new[] { From("test-stream", 0, 100, "TestEvent", []) };
// 		IndexEvents(events, shouldCommit);
//
// 		// When
// 		var result = await ReadForwards(startFrom: new TFPos(100, 100), maxCount: 10, validationStreamVersion: 7);
//
// 		// Then
// 		AssertEqual(
// 			ReadForwardCompleted(
// 				result: ReadIndexResult.Success,
// 				events: [],
// 				fromEventNumber: 5,
// 				maxCount: 10,
// 				nextEventNumber: 1,
// 				tfLastCommitPosition: 100,
// 				lastEventNumber: 7L, // Should use ValidationStreamVersion
// 				isEndOfStream: true
// 			),
// 			result
// 		);
// 	}
//
// 	[Theory]
// 	[InlineData(true)]
// 	[InlineData(false)]
// 	public async Task WhenSuccessfullyReadingWithMoreEventsAvailable_ReturnsCorrectResult(bool shouldCommit) {
// 		// Given
// 		var events = new[] {
// 			From("test-stream", 0, 100, "TestEvent", []),
// 			From("test-stream", 1, 200, "TestEvent", []),
// 			From("test-stream", 2, 300, "TestEvent", [])
// 		};
// 		IndexEvents(events, shouldCommit);
//
// 		// When
// 		var result = await ReadForwards(maxCount: 2);
//
// 		// Then
// 		AssertEqual(
// 			ReadForwardCompleted(
// 				result: ReadIndexResult.Success,
// 				events: events.Take(2).ToArray(),
// 				maxCount: 2,
// 				nextEventNumber: 2L,
// 				tfLastCommitPosition: 300,
// 				lastEventNumber: 2L,
// 				isEndOfStream: false
// 			),
// 			result
// 		);
// 	}
//
// 	[Theory]
// 	[InlineData(true)]
// 	[InlineData(false)]
// 	public async Task WhenAtEndOfStream_ReturnsCorrectResult(bool shouldCommit) {
// 		// Given
// 		var events = new[] {
// 			From("test-stream", 0, 100, "TestEvent", []),
// 			From("test-stream", 1, 200, "TestEvent", [])
// 		};
// 		IndexEvents(events, shouldCommit);
//
// 		// When
// 		var result = await ReadForwards(fromEventNumber: 1, maxCount: 10);
//
// 		// Then
// 		AssertEqual(
// 			ReadForwardCompleted(
// 				result: ReadIndexResult.Success,
// 				events: [events[1]],
// 				fromEventNumber: 1,
// 				maxCount: 10,
// 				nextEventNumber: 2L,
// 				tfLastCommitPosition: 200,
// 				lastEventNumber: 1L,
// 				isEndOfStream: true
// 			),
// 			result
// 		);
// 	}
//
// 	[Theory]
// 	[InlineData(true)]
// 	[InlineData(false)]
// 	public async Task WhenEventsExist_SetsNextEventNumberFromLastEvent(bool shouldCommit) {
// 		// Given
// 		var events = new[] {
// 			From("test-stream", 0, 100, "TestEvent", []),
// 			From("test-stream", 1, 200, "TestEvent", []),
// 			From("test-stream", 2, 300, "TestEvent", [])
// 		};
// 		IndexEvents(events, shouldCommit);
//
// 		// When
// 		var result = await ReadForwards(maxCount: 3);
//
// 		// Then
// 		AssertEqual(
// 			ReadForwardCompleted(
// 				result: ReadIndexResult.Success,
// 				events: events,
// 				maxCount: 3,
// 				nextEventNumber: 3L, // Last event number (2) + 1
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
// 	public async Task WhenOverTheEnd_ReturnsEmptyResult(bool shouldCommit) {
// 		// Given
// 		var events = new[] {
// 			From("test-stream", 0, 100, "TestEvent", []),
// 			From("test-stream", 1, 200, "TestEvent", []),
// 			From("test-stream", 2, 300, "TestEvent", [])
// 		};
// 		IndexEvents(events, shouldCommit);
//
// 		// When
// 		var result = await ReadForwards(fromEventNumber: long.MaxValue, maxCount: 1000);
//
// 		// Then
// 		AssertEqual(
// 			ReadForwardCompleted(
// 				result: ReadIndexResult.Success,
// 				events: [],
// 				maxCount: 1000,
// 				fromEventNumber: long.MaxValue,
// 				nextEventNumber: 3L, // Last event number (2) + 1
// 				tfLastCommitPosition: 300,
// 				lastEventNumber: 2L,
// 				isEndOfStream: true
// 			),
// 			result
// 		);
// 	}
//
// 	private async Task<ClientMessage.ReadStreamEventsForwardCompleted> ReadForwards(
// 		TFPos? startFrom = null,
// 		int maxCount = 10,
// 		bool requireLeader = true,
// 		long? validationStreamVersion = null,
// 		ClaimsPrincipal? user = null,
// 		bool replyOnExpired = false,
// 		TimeSpan? longPollTimeout = null,
// 		DateTime? expires = null
// 	) {
// 		var start = startFrom ?? TFPos.FirstRecordOfTf;
// 		var tcs = new TaskCompletionSource<ClientMessage.ReadStreamEventsForwardCompleted>();
// 		var envelope = new CallbackEnvelope(m => {
// 			Assert.IsType<ClientMessage.ReadStreamEventsForwardCompleted>(m);
// 			tcs.SetResult((ClientMessage.ReadStreamEventsForwardCompleted)m);
// 		});
//
// 		var msg = new ClientMessage.ReadIndexEventsForward(
// 			InternalCorrId,
// 			CorrelationId,
// 			envelope,
// 			DefaultIndex.Name,
// 			start.CommitPosition,
// 			start.PreparePosition,
// 			maxCount,
// 			requireLeader,
// 			validationStreamVersion,
// 			user,
// 			replyOnExpired,
// 			longPollTimeout,
// 			expires,
// 			CancellationToken.None
// 		);
//
// 		var result = await Sut.ReadForwards(msg, CancellationToken.None);
// 		envelope.ReplyWith(result);
//
// 		return await tcs.Task;
// 	}
//
// 	ClientMessage.ReadIndexEventsForwardCompleted ReadForwardCompleted(
// 		ReadIndexResult result,
// 		IReadOnlyList<ResolvedEvent> events,
// 		TFPos currentPosition,
// 		TFPos nextPosition,
// 		TFPos previousPosition,
// 		int maxCount = 10,
// 		bool isEndOfStream = false,
// 		long tfLastCommitPosition = -1,
// 		string error = ""
// 	) =>
// 		new(
// 			CorrelationId,
// 			result,
// 			error,
// 			events,
// 			maxCount,
// 			currentPosition,
// 			nextPosition,
// 			previousPosition,
// 			tfLastCommitPosition,
// 			isEndOfStream
// 		);
//
// 	private static void AssertEqual(
// 		ClientMessage.ReadStreamEventsForwardCompleted expected,
// 		ClientMessage.ReadStreamEventsForwardCompleted actual
// 	) {
// 		Assert.Equal(expected.CorrelationId, actual.CorrelationId);
// 		Assert.Equal(expected.EventStreamId, actual.EventStreamId);
// 		Assert.Equal(expected.FromEventNumber, actual.FromEventNumber);
// 		Assert.Equal(expected.MaxCount, actual.MaxCount);
// 		Assert.Equal(expected.Result, actual.Result);
// 		Assert.Equivalent(expected.Events.Select(e => e.Event), actual.Events.Select(e => e.Event).ToList());
// 		// Assert.Equal(expected.StreamMetadata, actual.StreamMetadata);
// 		Assert.Equal(expected.IsCachePublic, actual.IsCachePublic);
// 		Assert.Equal(expected.Error, actual.Error);
// 		Assert.Equal(expected.NextEventNumber, actual.NextEventNumber);
// 		Assert.Equal(expected.LastEventNumber, actual.LastEventNumber);
// 		Assert.Equal(expected.IsEndOfStream, actual.IsEndOfStream);
// 		Assert.Equal(expected.TfLastCommitPosition, actual.TfLastCommitPosition);
// 	}
// }
