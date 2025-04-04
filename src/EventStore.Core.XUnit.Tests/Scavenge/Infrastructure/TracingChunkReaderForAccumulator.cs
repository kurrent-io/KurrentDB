// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Threading;
using EventStore.Core.TransactionLog.Scavenging;
using EventStore.Core.TransactionLog.Scavenging.Interfaces;

namespace EventStore.Core.XUnit.Tests.Scavenge.Infrastructure;

public class TracingChunkReaderForAccumulator<TStreamId> : IChunkReaderForAccumulator<TStreamId> {
	private readonly IChunkReaderForAccumulator<TStreamId> _wrapped;
	private readonly Action<string> _trace;

	public TracingChunkReaderForAccumulator(
		IChunkReaderForAccumulator<TStreamId> wrapped,
		Action<string> trace) {

		_wrapped = wrapped;
		_trace = trace;
	}

	public IAsyncEnumerable<AccumulatorRecordType> ReadChunkInto(
		int logicalChunkNumber,
		RecordForAccumulator<TStreamId>.OriginalStreamRecord originalStreamRecord,
		RecordForAccumulator<TStreamId>.MetadataStreamRecord metadataStreamRecord,
		RecordForAccumulator<TStreamId>.TombStoneRecord tombStoneRecord,
		CancellationToken token) {

		var ret = _wrapped.ReadChunkInto(
			logicalChunkNumber,
			originalStreamRecord,
			metadataStreamRecord,
			tombStoneRecord,
			token);

		_trace($"Reading Chunk {logicalChunkNumber}");
		return ret;
	}
}
