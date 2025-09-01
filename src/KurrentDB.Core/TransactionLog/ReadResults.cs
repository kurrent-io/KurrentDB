// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.TransactionLog.LogRecords;
using KurrentDB.LogCommon;

namespace KurrentDB.Core.TransactionLog;

public readonly struct RecordReadResult {
	public static readonly RecordReadResult Failure = new(false, -1, null, 0);

	public readonly bool Success;
	public readonly long NextPosition;
	public readonly ILogRecord LogRecord;
	public readonly int RecordLength;

	public RecordReadResult(bool success, long nextPosition, ILogRecord logRecord, int recordLength) {
		Success = success;
		LogRecord = logRecord;
		NextPosition = nextPosition;
		RecordLength = recordLength;
	}

	public override string ToString() {
		return string.Format("Success: {0}, NextPosition: {1}, RecordLength: {2}, LogRecord: {3}",
			Success,
			NextPosition,
			RecordLength,
			LogRecord);
	}
}

public readonly struct RawReadResult {
	public static readonly RawReadResult Failure = new RawReadResult(false, -1, null, 0);

	public readonly bool Success;
	public readonly long NextPosition;
	public readonly byte[] RecordBuffer; // can be longer than the record
	public readonly int RecordLength;

	public LogRecordType RecordType => (LogRecordType)RecordBuffer[0];

	public RawReadResult(bool success, long nextPosition, byte[] record, int recordLength) {
		Success = success;
		RecordBuffer = record;
		NextPosition = nextPosition;
		RecordLength = recordLength;
	}

	public override string ToString() {
		return $"Success: {Success}, NextPosition: {NextPosition}, Record Length: {RecordLength}";
	}
}

public readonly struct SeqReadResult {
	public static SeqReadResult Failure =>
		default(SeqReadResult) with { RecordPrePosition = -1L, RecordPostPosition = -1L };

	public required ILogRecord LogRecord { get; init; }

	public required int RecordLength { get; init; }

	public required bool Eof { get; init; }

	public required long RecordPrePosition { get; init; }

	public required long RecordPostPosition { get; init; }

	public bool Success => LogRecord is not null;

	public override string ToString() {
		return string.Format(
			"Success: {0}, RecordLength: {1}, RecordPrePosition: {2}, RecordPostPosition: {3}, LogRecord: {4}",
			Success,
			RecordLength,
			RecordPrePosition,
			RecordPostPosition,
			LogRecord);
	}
}
