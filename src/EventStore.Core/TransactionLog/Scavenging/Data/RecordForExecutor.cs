// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.TransactionLog.LogRecords;

namespace EventStore.Core.TransactionLog.Scavenging;

// this abstracts the actual log records from the chunk execution logic
// TRecord is currently used as a LogRecord but in the future could just be bytes
public abstract class RecordForExecutor<TStreamId, TRecord> {
	public int Length { get; private set; }
	public TRecord Record { get; private set; }

	public class Prepare : RecordForExecutor<TStreamId, TRecord> {
		public void SetRecord(
			int length,
			long logPosition,
			TRecord record,
			DateTime timeStamp,
			TStreamId streamId,
			bool isSelfCommitted,
			bool isTombstone,
			bool isTransactionBegin,
			long eventNumber) {

			Length = length;
			LogPosition = logPosition;
			Record = record;
			TimeStamp = timeStamp;
			StreamId = streamId;
			IsSelfCommitted = isSelfCommitted;
			IsTombstone = isTombstone;
			IsTransactionBegin = isTransactionBegin;
			EventNumber = eventNumber;
		}

		public long LogPosition { get; private set; }
		public DateTime TimeStamp { get; private set; }
		public TStreamId StreamId { get; private set; }
		public bool IsSelfCommitted { get; private set; }
		public bool IsTombstone { get; private set; }
		public bool IsTransactionBegin { get; private set; }
		public long EventNumber { get; private set; }
	}

	public class NonPrepare : RecordForExecutor<TStreamId, TRecord> {
		public void SetRecord(
			int length,
			TRecord record) {

			Length = length;
			Record = record;
		}
	}
}
