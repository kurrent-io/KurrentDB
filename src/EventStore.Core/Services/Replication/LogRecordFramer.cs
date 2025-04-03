// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DotNext.IO;
using EventStore.Common.Utils;
using EventStore.Core.TransactionLog.LogRecords;
using EventStore.Transport.Tcp.Framing;

namespace EventStore.Core.Services.Replication;

internal sealed class LogRecordFramer : IAsyncMessageFramer<ILogRecord> {
	private readonly IAsyncMessageFramer<ReadOnlySequence<byte>> _inner;
	private Func<ILogRecord, CancellationToken, ValueTask> _handler = static (_, _) => ValueTask.CompletedTask;

	public LogRecordFramer(IAsyncMessageFramer<ReadOnlySequence<byte>> inner) {
		_inner = inner;
		_inner.RegisterMessageArrivedCallback(OnMessageArrived);
	}

	public bool HasData => _inner.HasData;

	public IEnumerable<ArraySegment<byte>> FrameData(ArraySegment<byte> data) => _inner.FrameData(data);

	public ValueTask UnFrameData(IEnumerable<ArraySegment<byte>> data, CancellationToken token) => _inner.UnFrameData(data, token);

	public ValueTask UnFrameData(ArraySegment<byte> data, CancellationToken token) => _inner.UnFrameData(data, token);

	public void Reset() => _inner.Reset();

	private ValueTask OnMessageArrived(ReadOnlySequence<byte> recordPayload, CancellationToken token) {
		var reader = new SequenceReader(recordPayload);
		var record = LogRecord.ReadFrom(ref reader);
		return _handler(record, token);
	}

	public void RegisterMessageArrivedCallback(Func<ILogRecord, CancellationToken, ValueTask> handler) {
		_handler = Ensure.NotNull(handler);
	}
}
