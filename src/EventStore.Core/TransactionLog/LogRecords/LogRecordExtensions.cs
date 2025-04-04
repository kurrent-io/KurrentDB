using EventStore.LogCommon;

namespace EventStore.Core.TransactionLog.LogRecords;

public static class LogRecordExtensions {
	public static bool IsTransactionBoundary(this ILogRecord rec) {
		return rec.RecordType switch {
			LogRecordType.Stream => false, // log v3 stream record
			LogRecordType.EventType => false, // log v3 event type record
			LogRecordType.Prepare => rec is IPrepareLogRecord prepare &&
									 (!prepare.Flags.HasFlag(PrepareFlags.IsCommitted) || // explicit transaction
									 prepare.Flags.HasFlag(PrepareFlags.TransactionEnd)), // last prepare in implicit transaction
			_ => true
		};
	}
}
