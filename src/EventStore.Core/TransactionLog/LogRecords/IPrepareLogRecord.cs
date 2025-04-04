using System;

namespace EventStore.Core.TransactionLog.LogRecords {
	// This interface specifies what the Storage, TF and Index machinery requires
	// in order to handle a prepare (i.e. data) record.
	// The V2 prepare implements it trivially
	public interface IPrepareLogRecord : ILogRecord {
		int SizeOnDisk { get; }
		PrepareFlags Flags { get; }
		long TransactionPosition { get; }
		int TransactionOffset { get; }
		long ExpectedVersion { get; }
		Guid EventId { get; }
		Guid CorrelationId { get; }
		DateTime TimeStamp { get; }
		ReadOnlyMemory<byte> Data { get; }
		ReadOnlyMemory<byte> Metadata { get; }
	}

	public interface IPrepareLogRecord<TStreamId> : IPrepareLogRecord {
		TStreamId EventStreamId { get; }
		TStreamId EventType { get; }

		IPrepareLogRecord<TStreamId> CopyForRetry(long logPosition, long transactionPosition);
	}
}
