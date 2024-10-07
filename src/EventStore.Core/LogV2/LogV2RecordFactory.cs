// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using EventStore.Core.LogAbstraction;
using EventStore.Core.TransactionLog.LogRecords;

namespace EventStore.Core.LogV2;

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
		ReadOnlyMemory<byte> metadata) {

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
			metadata: metadata);
		return result;
	}
}
