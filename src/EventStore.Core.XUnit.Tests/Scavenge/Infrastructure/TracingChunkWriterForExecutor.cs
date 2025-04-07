// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.TransactionLog.Scavenging;

namespace EventStore.Core.XUnit.Tests.Scavenge;

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

	public string FileName => _wrapped.FileName;

	public void WriteRecord(RecordForExecutor<TStreamId, TRecord> record) {
		_wrapped.WriteRecord(record);
	}

	public async ValueTask<(string, long)> Complete(CancellationToken token) {
		var result = await _wrapped.Complete(token);
		_tracer.Trace($"Switched in {Path.GetFileName(result.NewFileName)}");

		return result;
	}

	public void Abort(bool deleteImmediately) {
		_wrapped.Abort(deleteImmediately);
	}
}
