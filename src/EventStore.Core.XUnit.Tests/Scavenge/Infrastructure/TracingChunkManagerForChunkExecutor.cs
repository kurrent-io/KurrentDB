// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.TransactionLog.Chunks.TFChunk;
using EventStore.Core.TransactionLog.Scavenging.Interfaces;

namespace EventStore.Core.XUnit.Tests.Scavenge.Infrastructure;

public class TracingChunkManagerForChunkExecutor<TStreamId, TRecord, TChunk> :
	IChunkManagerForChunkExecutor<TStreamId, TRecord, TChunk> where TChunk : IChunkBlob {

	private readonly IChunkManagerForChunkExecutor<TStreamId, TRecord, TChunk> _wrapped;
	private readonly HashSet<int> _remoteChunks;
	private readonly Tracer _tracer;

	public TracingChunkManagerForChunkExecutor(
		IChunkManagerForChunkExecutor<TStreamId, TRecord, TChunk> wrapped,
		HashSet<int> remoteChunks,
		Tracer tracer) {

		_wrapped = wrapped;
		_remoteChunks = remoteChunks;
		_tracer = tracer;
	}

	public async ValueTask<IChunkWriterForExecutor<TStreamId, TRecord, TChunk>> CreateChunkWriter(
		IChunkReaderForExecutor<TStreamId, TRecord> sourceChunk,
		CancellationToken token) {

		return new TracingChunkWriterForExecutor<TStreamId, TRecord, TChunk>(
			await _wrapped.CreateChunkWriter(sourceChunk, token),
			_tracer);
	}

	public IChunkReaderForExecutor<TStreamId, TRecord> GetChunkReaderFor(long position) {
		var reader = _wrapped.GetChunkReaderFor(position);
		var isRemote = _remoteChunks.Contains(reader.ChunkStartNumber);
		return new TrackingChunkReaderForExecutor<TStreamId, TRecord>(reader, isRemote, _tracer);
	}

	public async ValueTask<string> SwitchInTempChunk(TChunk chunk, CancellationToken token) {
		var newFileName = await _wrapped.SwitchInTempChunk(chunk, token);
		_tracer.Trace($"Switched in {Path.GetFileName(newFileName)}");
		return newFileName;
	}
}
