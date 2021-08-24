﻿using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using EventStore.Common.Utils;
using EventStore.Core.LogV3;
using EventStore.LogCommon;
using EventStore.LogV3;
using StreamId = System.UInt32;

namespace EventStore.Core.TransactionLog.LogRecords {
	public class LogV3StreamWriteRecord : LogV3Record<StreamWriteRecord>, IEquatable<LogV3StreamWriteRecord>, IPrepareLogRecord<StreamId> {
		public LogV3StreamWriteRecord(ReadOnlyMemory<byte> bytes) : base() {
			Record = new StreamWriteRecord(new RecordView<Raw.StreamWriteHeader>(bytes));
		}

		public LogV3StreamWriteRecord(
			long logPosition,
			Guid correlationId,
			StreamId eventStreamId,
			long expectedVersion,
			DateTime timeStamp,
			PrepareFlags prepareFlags,
			IEventRecord[] events) {

			Ensure.Nonnegative(logPosition, "logPosition");
			Ensure.NotEmptyGuid(correlationId, "correlationId");
			if (eventStreamId < LogV3SystemStreams.FirstVirtualStream)
				throw new ArgumentOutOfRangeException("eventStreamId", eventStreamId, null);
			if (expectedVersion < Core.Data.ExpectedVersion.Any)
				throw new ArgumentOutOfRangeException("expectedVersion");

			foreach (var eventRecord in events) {
				Ensure.NotEmptyGuid(eventRecord.EventId, "eventId");
			}

			Record = RecordCreator.CreateStreamWriteRecord(
				timeStamp: timeStamp,
				correlationId: correlationId,
				logPosition: logPosition,
				prepareFlags: (ushort) prepareFlags,
				streamNumber: eventStreamId,
				startingEventNumber: expectedVersion + 1,
				events: events);
		}

		public override LogRecordType RecordType => LogRecordType.Prepare;

		// todo: translate
		public PrepareFlags Flags => (PrepareFlags)MemoryMarshal.AsRef<ushort>(Record.SystemMetadata.PrepareFlags.Span);
		public long TransactionPosition => LogPosition;
		public int TransactionOffset => 0;
		public long ExpectedVersion => Record.WriteId.StartingEventNumber - 1;
		public void PopulateExpectedVersionFromCommit(long commitFirstEventNumber) => Debug.Assert(false); //should not be executed for Log V3
		public void PopulateExpectedVersion(long expectedVersion) => Debug.Assert(expectedVersion == ExpectedVersion);

		public StreamId EventStreamId => Record.WriteId.StreamNumber;
		public Guid CorrelationId => Record.SystemMetadata.CorrelationId;
		public IEventRecord[] Events => Record.Events;

		public IPrepareLogRecord<StreamId> CopyForRetry(long logPosition, long transactionPosition) {
			return new LogV3StreamWriteRecord(
				logPosition: logPosition,
				correlationId: CorrelationId,
				eventStreamId: EventStreamId,
				expectedVersion: ExpectedVersion,
				timeStamp: TimeStamp,
				prepareFlags: Flags,
				events: Record.Events);
		}

		public bool Equals(LogV3StreamWriteRecord other) {
			if (ReferenceEquals(null, other)) return false;
			if (ReferenceEquals(this, other)) return true;
			return Record.Bytes.Span.SequenceEqual(other.Record.Bytes.Span);
		}

		public override bool Equals(object obj) {
			if (ReferenceEquals(null, obj)) return false;
			if (ReferenceEquals(this, obj)) return true;
			if (obj.GetType() != this.GetType()) return false;
			return Equals((LogV3StreamWriteRecord) obj);
		}
		public override int GetHashCode() => Record.Bytes.GetHashCode();
	}
}
