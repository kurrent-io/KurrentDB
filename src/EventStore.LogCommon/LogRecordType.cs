// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.LogCommon;

public enum LogRecordType : byte {
	Prepare = 0,
	Commit = 1,
	System = 2,
	PartitionType = 3,
	StreamType = 4,
	EventType = 5,
	ContentType = 6,
	Partition = 7,
	StreamWrite = 8,
	TransactionStart = 9,
	TransactionEnd = 10,
	Stream = 11,
}
