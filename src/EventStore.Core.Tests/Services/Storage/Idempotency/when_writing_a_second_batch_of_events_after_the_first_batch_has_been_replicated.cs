// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DotNext.Collections.Generic;
using EventStore.Core.Data;
using EventStore.Core.Services.Storage.ReaderIndex;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.Storage.Idempotency;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_writing_a_second_batch_of_events_after_the_first_batch_has_been_replicated<TLogFormat, TStreamId> : WriteEventsToIndexScenario<TLogFormat, TStreamId>{
	private const int _numEvents = 10;
	private List<Guid> _eventIds = new List<Guid>();
	private TStreamId _streamId = LogFormatHelper<TLogFormat, TStreamId>.StreamId;

	public override async ValueTask WriteEvents(CancellationToken token) {
		var expectedEventNumber = -1;
		var transactionPosition = 1000;
		var eventTypes = new List<TStreamId>();

		for (var i = 0; i < _numEvents; i++) {
			_eventIds.Add(Guid.NewGuid());
			eventTypes.Add(LogFormatHelper<TLogFormat, TStreamId>.EventTypeId);
		}

		var prepares = CreatePrepareLogRecords(_streamId, expectedEventNumber, eventTypes, _eventIds, transactionPosition);
		var commit = CreateCommitLogRecord(transactionPosition + 1000 * _numEvents, transactionPosition, expectedEventNumber + _numEvents);

		/*First batch write: committed to db and index*/
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
		var commitCheckResult = await _indexWriter.CheckCommit(_streamId, -1, _eventIds, streamMightExist: true, CancellationToken.None);
		Assert.AreEqual(CommitDecision.Idempotent, commitCheckResult.Decision);
	}

	[Test]
	public async Task check_commit_with_expectedversion_any_should_return_idempotent_decision() {
		/*Second, idempotent write*/
		var commitCheckResult = await _indexWriter.CheckCommit(_streamId, ExpectedVersion.Any, _eventIds, streamMightExist: true, CancellationToken.None);
		Assert.AreEqual(CommitDecision.Idempotent, commitCheckResult.Decision);
	}

	[Test]
	public async Task check_commit_with_next_expectedversion_should_return_ok_decision() {
		var commitCheckResult = await _indexWriter.CheckCommit(_streamId, _numEvents-1, _eventIds, streamMightExist: true, CancellationToken.None);
		Assert.AreEqual(CommitDecision.Ok, commitCheckResult.Decision);
	}

	[Test]
	public async Task check_commit_with_incorrect_expectedversion_should_return_wrongexpectedversion_decision() {
		var commitCheckResult = await _indexWriter.CheckCommit(_streamId, _numEvents, _eventIds, streamMightExist: true, CancellationToken.None);
		Assert.AreEqual(CommitDecision.WrongExpectedVersion, commitCheckResult.Decision);
	}

	[Test]
	public async Task check_commit_with_same_expectedversion_but_different_non_first_event_id_should_return_corruptedidempotency_decision() {
		/*Second, idempotent write but one of the event ids is different*/
		var ids = new List<Guid>();
		foreach(var id in _eventIds)
			ids.Add(id);

		ids[ids.Count-2] = Guid.NewGuid();

		var commitCheckResult = await _indexWriter.CheckCommit(_streamId, -1, ids, streamMightExist: true, CancellationToken.None);
		Assert.AreEqual(CommitDecision.CorruptedIdempotency, commitCheckResult.Decision);
	}

	[Test]
	public async Task check_commit_with_same_expectedversion_but_different_first_event_id_should_return_wrongexpectedversion_decision() {
		/*Second, idempotent write but one of the event ids is different*/
		var ids = new List<Guid>();
		foreach(var id in _eventIds)
			ids.Add(id);

		ids[0] = Guid.NewGuid();

		var commitCheckResult = await _indexWriter.CheckCommit(_streamId, -1, ids, streamMightExist: true, CancellationToken.None);
		Assert.AreEqual(CommitDecision.WrongExpectedVersion, commitCheckResult.Decision);
	}
    }
