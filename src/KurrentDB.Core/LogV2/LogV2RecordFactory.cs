// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Core.Data;
using KurrentDB.Core.LogAbstraction;
using KurrentDB.Core.TransactionLog.LogRecords;
using JetBrains.Annotations;

namespace KurrentDB.Core.LogV2;

public class LogV2RecordFactory : IRecordFactory<string> {
	public LogV2RecordFactory() {
	}

	public bool ExplicitStreamCreation => false;
	public bool ExplicitEventTypeCreation => false;

	public IPrepareLogRecord<string> CreateStreamRecord(
		Guid streamId,
		long logPosition,
		DateTime timeStamp,
		string streamNumber,
		string streamName) =>
		throw new NotSupportedException();

	public IPrepareLogRecord<string> CreateEventTypeRecord(
		Guid eventTypeId,
		Guid parentEventTypeId,
		string eventType,
		string referenceNumber,
		ushort version,
		long logPosition,
		DateTime timeStamp) =>
		throw new NotSupportedException();

	public ISystemLogRecord CreateEpoch(EpochRecord epoch) {
		var result = new SystemLogRecord(
			logPosition: epoch.EpochPosition,
			timeStamp: epoch.TimeStamp,
			systemRecordType: SystemRecordType.Epoch,
			systemRecordSerialization: SystemRecordSerialization.Json,
			data: epoch.AsSerialized());
		return result;
	}

	public IPrepareLogRecord<string> CreatePrepare(
		long logPosition,
		Guid correlationId,
		Guid eventId,
		long transactionPosition,
		int transactionOffset,
		string eventStreamId,
		long expectedVersion,
		DateTime timeStamp,
		PrepareFlags flags,
		string eventType,
		ReadOnlyMemory<byte> data,
		ReadOnlyMemory<byte> metadata,
        [CanBeNull] SchemaInfo dataSchemaInfo = null,
        [CanBeNull] SchemaInfo metadataSchemaInfo = null){

		var result = new PrepareLogRecord(
			logPosition: logPosition,
			correlationId: correlationId,
			eventId: eventId,
			transactionPosition: transactionPosition,
			transactionOffset: transactionOffset,
			eventStreamId: eventStreamId,
			eventStreamIdSize: null,
			expectedVersion: expectedVersion,
			timeStamp: timeStamp,
			flags: flags,
			eventType: eventType,
			eventTypeSize: null,
			data: data,
			metadata: metadata,
            dataSchemaInfo ?? SchemaInfo.None,
            metadataSchemaInfo ?? SchemaInfo.None);
		return result;
	}
}
