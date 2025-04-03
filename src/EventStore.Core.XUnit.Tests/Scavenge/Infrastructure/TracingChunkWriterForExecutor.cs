// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.TransactionLog.Scavenging;
using EventStore.Core.TransactionLog.Scavenging.Interfaces;

namespace EventStore.Core.XUnit.Tests.Scavenge.Infrastructure;

public class TracingChunkWriterForExecutor<TStreamId, TRecord> :
	IChunkWriterForExecutor<TStreamId, TRecord> {

	private readonly IChunkWriterForExecutor<TStreamId, TRecord> _wrapped;
	private readonly Tracer _tracer;

	public TracingChunkWriterForExecutor(
		IChunkWriterForExecutor<TStreamId, TRecord> wrapped,
		Tracer tracer) {

		_wrapped = wrapped;
		_tracer = tracer;
	}

	public string LocalFileName => _wrapped.LocalFileName;

	public ValueTask WriteRecord(RecordForExecutor<TStreamId, TRecord> record, CancellationToken token)
		=> _wrapped.WriteRecord(record, token);

	public async ValueTask<(string, long)> Complete(CancellationToken token) {
		var result = await _wrapped.Complete(token);
		_tracer.Trace($"Switched in {Path.GetFileName(result.NewFileName)}");

		return result;
	}

	public void Abort(bool deleteImmediately) {
		_wrapped.Abort(deleteImmediately);
	}
}
