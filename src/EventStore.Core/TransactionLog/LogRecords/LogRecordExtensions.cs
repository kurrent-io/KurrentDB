// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

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
