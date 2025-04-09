// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Core.Data;
using KurrentDB.Core.LogAbstraction;
using KurrentDB.Core.TransactionLog.LogRecords;
using KurrentDB.LogV3;
using JetBrains.Annotations;
using StreamId = System.UInt32;

namespace KurrentDB.Core.LogV3;

public class LogV3RecordFactory : IRecordFactory<StreamId> {
	private Guid _rootPartitionId;

	public LogV3RecordFactory() {
		if (!BitConverter.IsLittleEndian) {
			// to support big endian we would need to adjust some of the bit
			// operations in the raw v3 structs, and adjust the way that the
			// v3 records are written/read from disk (currently blitted)
			throw new NotSupportedException();
		}
	}

	public bool ExplicitStreamCreation => true;
	public bool ExplicitEventTypeCreation => true;

	public IPrepareLogRecord<StreamId> CreateStreamRecord(
		Guid streamId,
		long logPosition,
		DateTime timeStamp,
		StreamId streamNumber,
		string streamName) {

		var result = new LogV3StreamRecord(
			streamId: streamId,
			logPosition: logPosition,
			timeStamp: timeStamp,
			streamNumber: streamNumber,
			streamName: streamName,
			partitionId: _rootPartitionId);

		return result;
	}

	public IPrepareLogRecord<StreamId> CreateEventTypeRecord(
		Guid eventTypeId,
		Guid parentEventTypeId,
		string eventType,
		uint eventTypeNumber,
		ushort eventTypeVersion,
		long logPosition,
		DateTime timeStamp) {

		var result = new LogV3EventTypeRecord(
			eventTypeId: eventTypeId,
			parentEventTypeId: parentEventTypeId,
			eventType: eventType,
			eventTypeNumber: eventTypeNumber,
			eventTypeVersion: eventTypeVersion,
			logPosition: logPosition,
			timeStamp: timeStamp,
			partitionId: _rootPartitionId);

		return result;
	}

	public ISystemLogRecord CreateEpoch(EpochRecord epoch) {
		var result = new LogV3EpochLogRecord(
			logPosition: epoch.EpochPosition,
			timeStamp: epoch.TimeStamp,
			epochNumber: epoch.EpochNumber,
			epochId: epoch.EpochId,
			prevEpochPosition: epoch.PrevEpochPosition,
			leaderInstanceId: epoch.LeaderInstanceId);
		return result;
	}

	public IPrepareLogRecord<StreamId> CreatePrepare(
		long logPosition,
		Guid correlationId,
		Guid eventId,
		long transactionPosition,
		int transactionOffset,
		StreamId eventStreamId,
		long expectedVersion,
		DateTime timeStamp,
		PrepareFlags flags,
		StreamId eventType,
		ReadOnlyMemory<byte> data,
		ReadOnlyMemory<byte> metadata,
        [CanBeNull] SchemaInfo dataSchemaInfo = null,
        [CanBeNull] SchemaInfo metadataSchemaInfo = null) {

		var result = new LogV3StreamWriteRecord(
			logPosition: logPosition,
			transactionPosition: transactionPosition,
			transactionOffset: transactionOffset,
			correlationId: correlationId,
			eventId: eventId,
			eventStreamId: eventStreamId,
			expectedVersion: expectedVersion,
			timeStamp: timeStamp,
			flags: flags,
			eventType: eventType,
			data: data.Span,
			metadata: metadata.Span,
            dataSchemaInfo ?? SchemaInfo.None,
            metadataSchemaInfo ?? SchemaInfo.None);
		return result;
	}

	public ILogRecord CreatePartitionTypeRecord(
		DateTime timeStamp,
		long logPosition,
		Guid partitionTypeId,
		Guid partitionId,
		string name) {

		return new PartitionTypeLogRecord(
			timeStamp: timeStamp,
			logPosition: logPosition,
			partitionTypeId: partitionTypeId,
			partitionId: partitionId,
			name: name
		);
	}

	public ILogRecord CreatePartitionRecord(
		DateTime timeStamp,
		long logPosition,
		Guid partitionId,
		Guid partitionTypeId,
		Guid parentPartitionId,
		Raw.PartitionFlags flags,
		ushort referenceNumber,
		string name) {

		return new PartitionLogRecord(
			timeStamp: timeStamp,
			logPosition: logPosition,
			partitionId: partitionId,
			partitionTypeId: partitionTypeId,
			parentPartitionId: parentPartitionId,
			flags: flags,
			referenceNumber: referenceNumber,
			name: name
		);
	}

	public void SetRootPartitionId(Guid id) {
		_rootPartitionId = id;
	}
}
