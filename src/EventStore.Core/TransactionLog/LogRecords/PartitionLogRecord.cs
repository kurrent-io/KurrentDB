// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using EventStore.LogV3;

namespace EventStore.Core.TransactionLog.LogRecords;

public class PartitionLogRecord : LogV3Record<StringPayloadRecord<Raw.PartitionHeader>> {
	public PartitionLogRecord(DateTime timeStamp, long logPosition, Guid partitionId, Guid partitionTypeId,
		Guid parentPartitionId, Raw.PartitionFlags flags, ushort referenceNumber, string name) {

		Record = RecordCreator.CreatePartitionRecord(
			timeStamp,
			logPosition,
			partitionId,
			partitionTypeId,
			parentPartitionId,
			flags,
			referenceNumber,
			name);
	}

	public PartitionLogRecord(ReadOnlyMemory<byte> bytes) : base() {
		Record = StringPayloadRecord.Create(new RecordView<Raw.PartitionHeader>(bytes));
	}
}
