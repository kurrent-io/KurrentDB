// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.TransactionLog.Chunks;
using EventStore.Core.TransactionLog.Chunks.TFChunk;
using EventStore.Core.TransactionLog.LogRecords;

namespace EventStore.Core.TransactionLog.Scavenging.DbAccess;

public class ScavengedChunkWriter {
	const int BatchLength = 2000;
	private readonly List<List<PosMap>> _posMapss;
	private int _lastFlushedPage = -1;
	private readonly TFChunk _outputChunk;

	public ScavengedChunkWriter(TFChunk outputChunk) {
		_outputChunk = outputChunk;

		// list of lists to avoid having an enormous list which could make it to the LoH
		// and to avoid expensive resize operations on large lists
		_posMapss = new List<List<PosMap>>(capacity: BatchLength) {
			new(capacity: BatchLength)
		};
	}

	public async ValueTask WriteRecord(ILogRecord record, CancellationToken token) {
		var posMap = await TFChunkScavenger.WriteRecord(_outputChunk, record, token);

		// add the posmap in memory so we can write it when we complete
		var lastBatch = _posMapss[^1];
		if (lastBatch.Count >= BatchLength) {
			lastBatch = new List<PosMap>(capacity: BatchLength);
			_posMapss.Add(lastBatch);
		}

		lastBatch.Add(posMap);

		// occasionally flush the chunk. based on TFChunkScavenger.ScavengeChunk
		var currentPage = _outputChunk.RawWriterPosition / 4046;
		if (currentPage - _lastFlushedPage > TFChunkScavenger.FlushPageInterval) {
			await _outputChunk.Flush(token);
			_lastFlushedPage = currentPage;
		}
	}

	public async ValueTask Complete(CancellationToken token) {
		// write posmap
		var posMapCount = 0;
		foreach (var list in _posMapss)
			posMapCount += list.Count;

		var unifiedPosMap = new List<PosMap>(capacity: posMapCount);
		foreach (var list in _posMapss)
			unifiedPosMap.AddRange(list);

		await _outputChunk.CompleteScavenge(unifiedPosMap, token);
	}
}
