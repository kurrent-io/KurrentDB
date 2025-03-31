// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.TransactionLog.Chunks.TFChunk;
using EventStore.Core.TransactionLog.Scavenging;
using EventStore.Core.TransactionLog.Scavenging.Interfaces;

namespace EventStore.Core.XUnit.Tests.Scavenge.Infrastructure;

public class TracingChunkWriterForExecutor<TStreamId, TRecord, TChunk> :
	IChunkWriterForExecutor<TStreamId, TRecord, TChunk> where TChunk : IChunkBlob {

	private readonly IChunkWriterForExecutor<TStreamId, TRecord, TChunk> _wrapped;
	private readonly Tracer _tracer;

	public TracingChunkWriterForExecutor(
		IChunkWriterForExecutor<TStreamId, TRecord, TChunk> wrapped,
		Tracer tracer) {

		_wrapped = wrapped;
		_tracer = tracer;
	}

	public string LocalFileName => _wrapped.LocalFileName;

	public ValueTask WriteRecord(RecordForExecutor<TStreamId, TRecord> record, CancellationToken token)
		=> _wrapped.WriteRecord(record, token);

	public async ValueTask<TChunk> Complete(CancellationToken token) =>
		await _wrapped.Complete(token);

	public void Abort(bool deleteImmediately) {
		_tracer.Trace($"Aborted chunk writing. DeleteImmediately: {deleteImmediately}");
		_wrapped.Abort(deleteImmediately);
	}
}
