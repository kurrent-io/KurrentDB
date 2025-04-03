// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using DotNext.Buffers;
using EventStore.LogCommon;

namespace EventStore.Core.TransactionLog.LogRecords;

public interface ILogRecord {
	LogRecordType RecordType { get; }
	byte Version { get; }
	public long LogPosition { get; }
	void WriteTo(ref BufferWriterSlim<byte> writer);
	long GetNextLogPosition(long logicalPosition, int length);
	long GetPrevLogPosition(long logicalPosition, int length);
	int GetSizeWithLengthPrefixAndSuffix();
}
