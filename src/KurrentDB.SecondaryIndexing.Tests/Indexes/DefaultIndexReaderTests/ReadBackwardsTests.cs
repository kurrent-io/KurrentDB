// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Security.Claims;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Services;
using static KurrentDB.Core.Messages.ClientMessage;
using static KurrentDB.SecondaryIndexing.Tests.Fakes.TestResolvedEventFactory;

namespace KurrentDB.SecondaryIndexing.Tests.Indexes.DefaultIndexReaderTests;

public class ReadBackwardsTests : IndexTestBase {
	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task WhenNegativeStart_UsesEndOfLog(bool shouldCommit) {
		// Given
		var events = new[] {
			From("test-stream", 0, 100, "TestEvent", []),
			From("test-stream", 1, 200, "TestEvent", []),
			From("test-stream", 2, 300, "TestEvent", [])
		};
		IndexEvents(events, shouldCommit);

		// When
		var result = await ReadBackwards(startFrom: new TFPos(-1, -1), maxCount: 5);

		// Then
		AssertEqual(
			ReadStreamEventsBackwardCompleted(
				result: ReadIndexResult.Success,
				events: events.Reverse().ToArray(),
				tfLastCommitPosition: 300,
				isEndOfStream: true
			),
			result
		);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task WhenEmptyResultSet_ReturnsSuccessWithNoEvents(bool shouldCommit) {
		// Given
		var events = new[] { From("test-stream", 0, 100, "TestEvent", []) };
		IndexEvents(events, shouldCommit);

		// When - request events beyond what exists
		var result = await ReadBackwards(startFrom: new TFPos(50, 50), maxCount: 5);

		// Then
		AssertEqual(
			ReadStreamEventsBackwardCompleted(
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
	public async Task WhenEmptyNegativeStart_UsesValidationVersion(bool shouldCommit) {
		// Given
		var events = new[] { From("test-stream", 0, 100, "TestEvent", []) };
		IndexEvents(events, shouldCommit);

		// When
		var result = await ReadBackwards(new TFPos(-1, -1), maxCount: 5, validationStreamVersion: 3);

		// Then
		AssertEqual(
			ReadStreamEventsBackwardCompleted(
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
	public async Task WhenReadingFromLastMoreThanAvailable_ReturnsIsEndOfStreamTrue(bool shouldCommit) {
		// Given
		var events = new[] {
			From("test-stream", 0, 100, "TestEvent", []),
			From("test-stream", 1, 200, "TestEvent", []),
			From("test-stream", 2, 300, "TestEvent", [])
		};
		IndexEvents(events, shouldCommit);

		// When
		var result = await ReadBackwards(startFrom: new TFPos(300, 300), maxCount: 5);

		// Then
		AssertEqual(
			ReadStreamEventsBackwardCompleted(
				result: ReadIndexResult.Success,
				events: events.Reverse().ToArray(),
				tfLastCommitPosition: 300,
				isEndOfStream: true
			),
			result
		);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task WhenValidationVersionMatches_ReturnsNotModified(bool shouldCommit) {
		// Given
		var events = new[] { From("test-stream", 0, 100, "TestEvent", []) };
		IndexEvents(events, shouldCommit);

		// When
		var result = await ReadBackwards(validationStreamVersion: 100);

		// Then
		AssertEqual(
			ReadStreamEventsBackwardCompleted(
				result: ReadIndexResult.NotModified,
				events: [],
				tfLastCommitPosition: 100,
				isEndOfStream: false
			),
			result
		);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task WhenReadingFromEndMoreThanAvailable_ReturnsIsEndOfStreamTrue(bool shouldCommit) {
		// Given
		var events = new[] {
			From("test-stream", 0, 100, "TestEvent", []),
			From("test-stream", 1, 200, "TestEvent", []),
			From("test-stream", 2, 300, "TestEvent", [])
		};
		IndexEvents(events, shouldCommit);

		// When - request events that don't exist
		var result = await ReadBackwards(startFrom: new TFPos(long.MaxValue, long.MaxValue), maxCount: 1000);

		// Then
		AssertEqual(
			ReadStreamEventsBackwardCompleted(
				result: ReadIndexResult.Success,
				events: [],
				tfLastCommitPosition: 300,
				isEndOfStream: true
			),
			result
		);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task WhenMoreEventsAvailable_ReturnsOnlyRequested(bool shouldCommit) {
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
		var result = await ReadBackwards(startFrom: new TFPos(500, 500), maxCount: 2);

		// Then
		AssertEqual(
			ReadStreamEventsBackwardCompleted(
				result: ReadIndexResult.Success,
				events: [events[4], events[3]], // startEventNumber - 1 = 3 - 1 = 2
				tfLastCommitPosition: 500,
				isEndOfStream: false
			),
			result
		);
	}

	private async Task<ReadIndexEventsBackwardCompleted> ReadBackwards(
		TFPos? startFrom = null,
		int maxCount = 5,
		bool requireLeader = true,
		long? validationStreamVersion = null,
		ClaimsPrincipal? user = null,
		bool replyOnExpired = false,
		TimeSpan? longPollTimeout = null,
		DateTime? expires = null
	) {
		var start = startFrom ?? TFPos.FirstRecordOfTf;
		var tcs = new TaskCompletionSource<ReadIndexEventsBackwardCompleted>();
		var envelope = new CallbackEnvelope(m => {
			Assert.IsType<ReadIndexEventsBackwardCompleted>(m);
			tcs.SetResult((ReadIndexEventsBackwardCompleted)m);
		});

		var msg = new ReadIndexEventsBackward(
			InternalCorrId,
			CorrelationId,
			envelope,
			SystemStreams.DefaultSecondaryIndex,
			start.CommitPosition,
			start.PreparePosition,
			false,
			maxCount,
			requireLeader,
			validationStreamVersion,
			user,
			replyOnExpired,
			pool: null,
			longPollTimeout,
			expires,
			CancellationToken.None
		);

		var result = await Sut.ReadBackwards(msg, CancellationToken.None);
		envelope.ReplyWith(result);

		return await tcs.Task;
	}

	private static ReadIndexEventsBackwardCompleted ReadStreamEventsBackwardCompleted(
		ReadIndexResult result,
		IReadOnlyList<ResolvedEvent> events,
		bool isEndOfStream = false,
		long tfLastCommitPosition = 0,
		string? error = null
	) =>
		new(result, events, TFPos.FirstRecordOfTf, tfLastCommitPosition, isEndOfStream, error);

	private static void AssertEqual(
		ReadIndexEventsBackwardCompleted expected,
		ReadIndexEventsBackwardCompleted actual
	) {
		Assert.Equal(expected.Result, actual.Result);
		Assert.Equivalent(expected.Events.Select(e => e.Event), actual.Events.Select(e => e.Event).ToList());
		Assert.Equal(expected.Error, actual.Error);
		Assert.Equal(expected.IsEndOfStream, actual.IsEndOfStream);
		Assert.Equal(expected.TfLastCommitPosition, actual.TfLastCommitPosition);
	}
}
