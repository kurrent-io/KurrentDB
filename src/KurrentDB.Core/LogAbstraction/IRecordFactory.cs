// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Core.Data;
using KurrentDB.Core.TransactionLog.LogRecords;

namespace KurrentDB.Core.LogAbstraction;

public interface IRecordFactory {
	ISystemLogRecord CreateEpoch(EpochRecord epoch);
}

public interface IRecordFactory<TStreamId> : IRecordFactory {
	bool ExplicitStreamCreation { get; }
	bool ExplicitEventTypeCreation { get; }

	IPrepareLogRecord<TStreamId> CreateStreamRecord(
		Guid streamId,
		long logPosition,
		DateTime timeStamp,
		TStreamId streamNumber,
		string streamName);

	IPrepareLogRecord<TStreamId> CreateEventTypeRecord(
		Guid eventTypeId,
		Guid parentEventTypeId,
		string eventType,
		TStreamId eventTypeNumber,
		ushort eventTypeVersion,
		long logPosition,
		DateTime timeStamp);

	IPrepareLogRecord<TStreamId> CreatePrepare(
		long logPosition,
		Guid correlationId,
		Guid eventId,
		long transactionPosition,
		int transactionOffset,
		TStreamId eventStreamId,
		long expectedVersion,
		DateTime timeStamp,
		PrepareFlags flags,
		TStreamId eventType,
		ReadOnlyMemory<byte> data,
		ReadOnlyMemory<byte> metadata,
        SchemaInfo dataSchemaInfo,
        SchemaInfo metadataSchemaInfo);

	IPrepareLogRecord<TStreamId> CreatePrepare(
		long logPosition,
		Guid correlationId,
		Guid eventId,
		long transactionPosition,
		int transactionOffset,
		TStreamId eventStreamId,
		long expectedVersion,
		DateTime timeStamp,
		PrepareFlags flags,
		TStreamId eventType,
		ReadOnlyMemory<byte> data,
		ReadOnlyMemory<byte> metadata) {
		return CreatePrepare(logPosition, correlationId, eventId, transactionPosition, transactionOffset, eventStreamId,
			expectedVersion, timeStamp, flags, eventType, data, metadata, SchemaInfo.None, SchemaInfo.None);
	}
}
