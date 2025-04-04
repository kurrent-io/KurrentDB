using System;
using EventStore.Core.TransactionLog.LogRecords;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.Storage.Chaser {
	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class when_chaser_reads_commit_event<TLogFormat, TStreamId> : with_storage_chaser_service<TLogFormat, TStreamId> {
		private long _logPosition;
		private Guid _eventId;
		private Guid _transactionId;

		public override void When() {
			_eventId = Guid.NewGuid();
			_transactionId = Guid.NewGuid();

			var recordFactory = LogFormatHelper<TLogFormat, TStreamId>.RecordFactory;
			var streamId = LogFormatHelper<TLogFormat, TStreamId>.StreamId;
			var eventTypeId = LogFormatHelper<TLogFormat, TStreamId>.EventTypeId;

			var record = LogRecord.Prepare(
				factory: recordFactory,
				logPosition: 0,
				eventId: _eventId,
				correlationId: _transactionId,
				transactionPos: 0xDEAD,
				transactionOffset: 0xBEEF,
				eventStreamId: streamId,
				expectedVersion: 1234,
				timeStamp: new DateTime(2012, 12, 21),
				flags: PrepareFlags.Data,
				eventType: eventTypeId,
				data: new byte[] { 1, 2, 3, 4, 5 },
				metadata: new byte[] { 7, 17 });
			Assert.True(Writer.Write(record, out _logPosition));
			Writer.Flush();

			IndexCommitter.AddPendingPrepare(new[] { record }, _logPosition);
			var record2 = new CommitLogRecord(
				logPosition: _logPosition,
				correlationId: _transactionId,
				transactionPosition: 0,
				timeStamp: new DateTime(2012, 12, 21),
				firstEventNumber: 10);

			Assert.True(Writer.Write(record2, out _logPosition));
			Writer.Flush();
		}
		[Test]
		public void commit_ack_should_be_published() {
			AssertEx.IsOrBecomesTrue(() => CommitAcks.Count == 1, msg: "CommitAck msg not received");
			Assert.True(CommitAcks.TryDequeue(out var commitAck));
			Assert.AreEqual(_transactionId, commitAck.CorrelationId);
		}

	}
}
