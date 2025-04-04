using System;
using EventStore.Core.Data;
using EventStore.Core.Services.Storage.ReaderIndex;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.Storage.Idempotency {
	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class when_writing_a_second_event_after_the_first_event_has_not_yet_been_replicated<TLogFormat, TStreamId> : WriteEventsToIndexScenario<TLogFormat, TStreamId> {
		private Guid _eventId = Guid.NewGuid();
		private TStreamId _streamId = LogFormatHelper<TLogFormat, TStreamId>.StreamId;

		public override void WriteEvents() {
			var expectedEventNumber = -1;
			var transactionPosition = 1000;
			var eventTypeId = LogFormatHelper<TLogFormat, TStreamId>.EventTypeId;
			var prepares = CreatePrepareLogRecord(_streamId, expectedEventNumber, eventTypeId, _eventId, transactionPosition);
			var commit = CreateCommitLogRecord(transactionPosition + RecordOffset, transactionPosition, expectedEventNumber + 1);

			/*First write: committed to db and pre-committed to index but not yet committed to index*/
			WriteToDB(prepares);
			PreCommitToIndex(prepares);

			WriteToDB(commit);
			PreCommitToIndex(commit);
		}

		[Test]
		public void check_commit_with_same_expectedversion_should_return_idempotentnotready_decision() {
			/*Second, idempotent write*/
			var commitCheckResult = _indexWriter.CheckCommit(_streamId, -1, new Guid[] { _eventId }, streamMightExist: true);
			Assert.AreEqual(CommitDecision.IdempotentNotReady, commitCheckResult.Decision);
		}

		[Test]
		public void check_commit_with_expectedversion_any_should_return_idempotentnotready_decision() {
			/*Second, idempotent write*/
			var commitCheckResult = _indexWriter.CheckCommit(_streamId, ExpectedVersion.Any, new Guid[] { _eventId }, streamMightExist: true);
			Assert.AreEqual(CommitDecision.IdempotentNotReady, commitCheckResult.Decision);
		}

		[Test]
		public void check_commit_with_next_expectedversion_should_return_ok_decision() {
			/*Second, idempotent write*/
			var commitCheckResult = _indexWriter.CheckCommit(_streamId, 0, new Guid[] { _eventId }, streamMightExist: true);
			Assert.AreEqual(CommitDecision.Ok, commitCheckResult.Decision);
		}

		[Test]
		public void check_commit_with_incorrect_expectedversion_should_return_wrongexpectedversion_decision() {
			/*Second, idempotent write*/
			var commitCheckResult = _indexWriter.CheckCommit(_streamId, 1, new Guid[] { _eventId }, streamMightExist: true);
			Assert.AreEqual(CommitDecision.WrongExpectedVersion, commitCheckResult.Decision);
		}
	}
}
