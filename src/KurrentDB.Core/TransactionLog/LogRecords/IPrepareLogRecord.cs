// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Core.Data;

namespace KurrentDB.Core.TransactionLog.LogRecords;

// This interface specifies what the Storage, TF and Index machinery requires
// in order to handle a prepare (i.e. data) record.
// The V2 prepare implements it trivially
public interface IPrepareLogRecord : ILogRecord {
	PrepareFlags Flags { get; }
	long TransactionPosition { get; }
	int TransactionOffset { get; }
	long ExpectedVersion { get; }
	Guid EventId { get; }
	Guid CorrelationId { get; }
	DateTime TimeStamp { get; }
	ReadOnlyMemory<byte> Data { get; }
	ReadOnlyMemory<byte> Metadata { get; }
	SchemaInfo DataSchemaInfo { get; }
	SchemaInfo MetadataSchemaInfo { get; }
}

public interface IPrepareLogRecord<TStreamId> : IPrepareLogRecord {
	TStreamId EventStreamId { get; }
	TStreamId EventType { get; }

	IPrepareLogRecord<TStreamId> CopyForRetry(long logPosition, long transactionPosition);
}
