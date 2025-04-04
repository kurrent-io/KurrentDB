// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using EventStore.LogV3;

namespace EventStore.Core.TransactionLog.LogRecords;

public class PartitionTypeLogRecord : LogV3Record<StringPayloadRecord<Raw.PartitionTypeHeader>> {
	public PartitionTypeLogRecord(DateTime timeStamp, long logPosition, Guid partitionTypeId, Guid partitionId,
		string name) {

		Record = RecordCreator.CreatePartitionTypeRecord(
			timeStamp,
			logPosition,
			partitionTypeId,
			partitionId,
			name);
	}

	public PartitionTypeLogRecord(ReadOnlyMemory<byte> bytes) : base() {
		Record = StringPayloadRecord.Create(new RecordView<Raw.PartitionTypeHeader>(bytes));
	}
}
