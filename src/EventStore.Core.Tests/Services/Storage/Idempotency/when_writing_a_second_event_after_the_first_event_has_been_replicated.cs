// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using DotNext.Collections.Generic;
using EventStore.Core.Data;
using EventStore.Core.Services.Storage.ReaderIndex;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.Storage.Idempotency;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_writing_a_second_event_after_the_first_event_has_been_replicated<TLogFormat, TStreamId> : WriteEventsToIndexScenario<TLogFormat, TStreamId>{
	private Guid _eventId = Guid.NewGuid();
	private TStreamId _streamId = LogFormatHelper<TLogFormat, TStreamId>.StreamId;

	public override async ValueTask WriteEvents(CancellationToken token) {
		var expectedEventNumber = -1;
		var transactionPosition = 1000;
		var eventTypeId = LogFormatHelper<TLogFormat, TStreamId>.EventTypeId;
		var prepares = CreatePrepareLogRecord(_streamId, expectedEventNumber, eventTypeId, _eventId, transactionPosition);
		var commit = CreateCommitLogRecord(transactionPosition + RecordOffset, transactionPosition, expectedEventNumber + 1);

		/*First write: committed to db and index*/
		WriteToDB(prepares);
		PreCommitToIndex(prepares);

		WriteToDB(commit);
		await PreCommitToIndex(commit, token);

		await CommitToIndex(prepares, token);
		await CommitToIndex(commit, token);
	}

	[Test]
	public async Task check_commit_with_same_expectedversion_should_return_idempotent_decision() {
		/*Second, idempotent write*/
		var commitCheckResult = await _indexWriter.CheckCommit(_streamId, -1, [_eventId], streamMightExist: true, CancellationToken.None);
		Assert.AreEqual(CommitDecision.Idempotent, commitCheckResult.Decision);
	}

	[Test]
	public async Task check_commit_with_expectedversion_any_should_return_idempotent_decision() {
		/*Second, idempotent write*/
		var commitCheckResult = await _indexWriter.CheckCommit(_streamId, ExpectedVersion.Any, [_eventId], streamMightExist: true, CancellationToken.None);
		Assert.AreEqual(CommitDecision.Idempotent, commitCheckResult.Decision);
	}

	[Test]
	public async Task check_commit_with_next_expectedversion_should_return_ok_decision() {
		var commitCheckResult = await _indexWriter.CheckCommit(_streamId, 0, [_eventId], streamMightExist: true, CancellationToken.None);
		Assert.AreEqual(CommitDecision.Ok, commitCheckResult.Decision);
	}

	[Test]
	public async Task check_commit_with_incorrect_expectedversion_should_return_wrongexpectedversion_decision() {
		var commitCheckResult = await _indexWriter.CheckCommit(_streamId, 1, [_eventId], streamMightExist: true, CancellationToken.None);
		Assert.AreEqual(CommitDecision.WrongExpectedVersion, commitCheckResult.Decision);
	}
}
