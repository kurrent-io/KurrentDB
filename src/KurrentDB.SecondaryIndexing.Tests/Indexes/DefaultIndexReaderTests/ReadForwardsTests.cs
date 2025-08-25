// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Security.Claims;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Services;
using static KurrentDB.Core.Messages.ClientMessage;
using static KurrentDB.SecondaryIndexing.Tests.Fakes.TestResolvedEventFactory;

namespace KurrentDB.SecondaryIndexing.Tests.Indexes.DefaultIndexReaderTests;

public class ReadForwardsTests : IndexTestBase {
	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task WhenValidationVersionMatches_ReturnsNotModified(bool shouldCommit) {
		// Given
		var events = new[] { From("test-stream", 0, 100, "TestEvent", []) };
		IndexEvents(events, shouldCommit);

		// When
		var result = await ReadForwards(validationTfLastCommitPosition: 100);

		// Then
		AssertEqual(
			ReadForwardCompleted(
				result: ReadIndexResult.NotModified,
				events: [],
				tfLastCommitPosition: 100,
				isEndOfStream: true
			),
			result
		);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task WhenMaxCountOverflow_HandlesCorrectly(bool shouldCommit) {
		// Given
		var events = new[] { From("test-stream", 0, 100, "TestEvent", []) };
		IndexEvents(events, shouldCommit);

		// When
		var pos = long.MaxValue - 5;
		var result = await ReadForwards(startFrom: new TFPos(pos, pos), maxCount: 10);

		// Then
		AssertEqual(
			ReadForwardCompleted(
				result: ReadIndexResult.Success,
				events: [],
				tfLastCommitPosition: 100,
				isEndOfStream: true
			),
			result
		);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task WhenRequestBeyondExistingSet_ReturnsSuccessWithNoEvents(bool shouldCommit) {
		// Given
		var events = new[] { From("test-stream", 0, 100, "TestEvent", []) };
		IndexEvents(events, shouldCommit);

		// When
		var result = await ReadForwards(startFrom: new TFPos(200, 200), maxCount: 10);

		// Then
		AssertEqual(
			ReadForwardCompleted(
				result: ReadIndexResult.Success,
				events: [],
				tfLastCommitPosition: 100,
				isEndOfStream: true
			),
			result
		);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task WhenRequestBeyondExistingSetWithValidationVersion_UsesValidationVersion(bool shouldCommit) {
		// Given
		var events = new[] { From("test-stream", 0, 100, "TestEvent", []) };
		IndexEvents(events, shouldCommit);

		// When
		var result = await ReadForwards(startFrom: new TFPos(100, 100), maxCount: 10, validationTfLastCommitPosition: 7);

		// Then
		AssertEqual(
			ReadForwardCompleted(
				result: ReadIndexResult.Success,
				events: [],
				tfLastCommitPosition: 100,
				isEndOfStream: true
			),
			result
		);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task WhenSuccessfullyReadingWithMoreEventsAvailable_ReturnsCorrectResult(bool shouldCommit) {
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
			ReadForwardCompleted(
				result: ReadIndexResult.Success,
				events: events.Take(2).ToArray(),
				tfLastCommitPosition: 300,
				isEndOfStream: false
			),
			result
		);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task WhenAtEndOfStream_ReturnsCorrectResult(bool shouldCommit) {
		// Given
		var events = new[] {
			From("test-stream", 0, 100, "TestEvent", []),
			From("test-stream", 1, 200, "TestEvent", [])
		};
		IndexEvents(events, shouldCommit);

		// When
		var result = await ReadForwards(startFrom: new TFPos(200, 200), maxCount: 10);

		// Then
		AssertEqual(
			ReadForwardCompleted(
				result: ReadIndexResult.Success,
				events: [events[1]],
				tfLastCommitPosition: 200,
				isEndOfStream: true
			),
			result
		);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task WhenEventsExist_SetsNextEventNumberFromLastEvent(bool shouldCommit) {
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
			ReadForwardCompleted(
				result: ReadIndexResult.Success,
				events: events,
				tfLastCommitPosition: 300,
				isEndOfStream: true
			),
			result
		);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task WhenOverTheEnd_ReturnsEmptyResult(bool shouldCommit) {
		// Given
		var events = new[] {
			From("test-stream", 0, 100, "TestEvent", []),
			From("test-stream", 1, 200, "TestEvent", []),
			From("test-stream", 2, 300, "TestEvent", [])
		};
		IndexEvents(events, shouldCommit);

		// When
		var result = await ReadForwards(startFrom: new TFPos(long.MaxValue, long.MaxValue), maxCount: 1000);

		// Then
		AssertEqual(
			ReadForwardCompleted(
				result: ReadIndexResult.Success,
				events: [],
				tfLastCommitPosition: 300,
				isEndOfStream: true
			),
			result
		);
	}

	private async Task<ReadIndexEventsForwardCompleted> ReadForwards(
		TFPos? startFrom = null,
		int maxCount = 10,
		bool requireLeader = true,
		long? validationTfLastCommitPosition = null,
		ClaimsPrincipal? user = null,
		bool replyOnExpired = false,
		TimeSpan? longPollTimeout = null,
		DateTime? expires = null
	) {
		var start = startFrom ?? TFPos.FirstRecordOfTf;
		var tcs = new TaskCompletionSource<ReadIndexEventsForwardCompleted>();
		var envelope = new CallbackEnvelope(m => {
			Assert.IsType<ReadIndexEventsForwardCompleted>(m);
			tcs.SetResult((ReadIndexEventsForwardCompleted)m);
		});

		var msg = new ReadIndexEventsForward(
			InternalCorrId,
			CorrelationId,
			envelope,
			SystemStreams.DefaultSecondaryIndex,
			start.CommitPosition,
			start.PreparePosition,
			false,
			maxCount,
			requireLeader,
			validationTfLastCommitPosition,
			user,
			replyOnExpired,
			longPollTimeout,
			expires,
			CancellationToken.None
		);

		var result = await Sut.ReadForwards(msg, CancellationToken.None);
		envelope.ReplyWith(result);

		return await tcs.Task;
	}

	static ReadIndexEventsForwardCompleted ReadForwardCompleted(
		ReadIndexResult result,
		IReadOnlyList<ResolvedEvent> events,
		bool isEndOfStream = false,
		long tfLastCommitPosition = -1,
		string? error = null
	) =>
		new(
			result,
			events,
			TFPos.FirstRecordOfTf,
			tfLastCommitPosition,
			isEndOfStream,
			error
		);

	static void AssertEqual(ReadIndexEventsForwardCompleted expected, ReadIndexEventsForwardCompleted actual) {
		Assert.Equal(expected.Result, actual.Result);
		Assert.Equivalent(expected.Events.Select(e => e.Event), actual.Events.Select(e => e.Event).ToList());
		Assert.Equal(expected.Error, actual.Error);
		Assert.Equal(expected.IsEndOfStream, actual.IsEndOfStream);
		Assert.Equal(expected.TfLastCommitPosition, actual.TfLastCommitPosition);
	}
}
