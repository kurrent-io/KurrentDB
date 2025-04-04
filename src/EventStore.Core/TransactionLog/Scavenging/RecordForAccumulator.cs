// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Data;

namespace EventStore.Core.TransactionLog.Scavenging;

public enum AccumulatorRecordType {
	OriginalStreamRecord,
	MetadataStreamRecord,
	TombstoneRecord,
}

public abstract class RecordForAccumulator<TStreamId> {
	public TStreamId StreamId { get; private set; }
	public long LogPosition { get; private set; }
	public DateTime TimeStamp { get; private set; }

	protected void Reset(TStreamId streamId, long logPosition, DateTime timeStamp) {
		StreamId = streamId;
		LogPosition = logPosition;
		TimeStamp = timeStamp;
	}

	// Record in original stream
	public class OriginalStreamRecord : RecordForAccumulator<TStreamId> {
		public new void Reset(TStreamId streamId, long logPosition, DateTime timeStamp) =>
			base.Reset(streamId, logPosition, timeStamp);
	}

	// Record in metadata stream
	public class MetadataStreamRecord : RecordForAccumulator<TStreamId> {
		public void Reset(
			TStreamId streamId,
			long logPosition,
			DateTime timeStamp,
			long eventNumber,
			StreamMetadata metadata) {

			Reset(streamId, logPosition, timeStamp);
			EventNumber = eventNumber;
			Metadata = metadata;
		}

		public StreamMetadata Metadata { get; private set; }
		public long EventNumber { get; private set; }
	}

	public class TombStoneRecord : RecordForAccumulator<TStreamId> {
		public void Reset(
			TStreamId streamId,
			long logPosition,
			DateTime timeStamp,
			long eventNumber) {

			Reset(streamId, logPosition, timeStamp);
			EventNumber = eventNumber;
		}

		// old scavenge, index writer and index committer are set up to handle
		// tombstones that have arbitrary event numbers, so let's handle them here
		// in case it used to be possible to create them.
		public long EventNumber { get; private set; }
	}
}
