// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Core.LogAbstraction;
using KurrentDB.Core.TransactionLog.LogRecords;

namespace KurrentDB.Core.Tests;

public static class RecordFactoryExtensions {
	public static IPrepareLogRecord<TStreamId> Prepare<TStreamId>(this IRecordFactory<TStreamId> factory,
		long logPosition,
		Guid correlationId,
		Guid eventId,
		long transactionPos,
		int transactionOffset,
		TStreamId eventStreamId,
		long expectedVersion,
		PrepareFlags flags,
		TStreamId eventType,
		byte[] data,
		byte[] metadata,
		DateTime? timeStamp = null) {
		return factory.CreatePrepare(logPosition, correlationId, eventId, transactionPos, transactionOffset,
			eventStreamId, expectedVersion, timeStamp ?? DateTime.UtcNow, flags, eventType,
			data, metadata);
	}
}
