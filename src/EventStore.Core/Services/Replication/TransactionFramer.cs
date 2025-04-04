// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Common.Utils;
using EventStore.Core.TransactionLog.LogRecords;
using EventStore.Transport.Tcp.Framing;

namespace EventStore.Core.Services.Replication;

internal sealed class TransactionFramer : IAsyncMessageFramer<IEnumerable<ILogRecord>> {
	private readonly List<ILogRecord> _records = new(capacity: 1024);
	private readonly IAsyncMessageFramer<ILogRecord> _inner;
	private Func<IEnumerable<ILogRecord>, CancellationToken, ValueTask> _handler = static (_, _) => ValueTask.CompletedTask;

	public TransactionFramer(IAsyncMessageFramer<ILogRecord> inner) {
		_inner = inner;
		_inner.RegisterMessageArrivedCallback(OnLogRecordUnframed);
	}

	public bool HasData => _records.Count > 0 || _inner.HasData;

	public IEnumerable<ArraySegment<byte>> FrameData(ArraySegment<byte> data) => _inner.FrameData(data);

	public ValueTask UnFrameData(IEnumerable<ArraySegment<byte>> data, CancellationToken token) => _inner.UnFrameData(data, token);

	public ValueTask UnFrameData(ArraySegment<byte> data, CancellationToken token) => _inner.UnFrameData(data, token);

	public void Reset() {
		_records.Clear();
		_inner.Reset();
	}

	private async ValueTask OnLogRecordUnframed(ILogRecord record, CancellationToken token) {
		_records.Add(record);
		if (record.IsTransactionBoundary()) {
			await _handler(_records, token);
			_records.Clear();
		}
	}

	public void RegisterMessageArrivedCallback(Func<IEnumerable<ILogRecord>, CancellationToken, ValueTask> handler) {
		Ensure.NotNull(handler, nameof(handler));
		_handler = handler;
	}

	public async ValueTask<int?> UnFramePendingLogRecords(CancellationToken token) {
		if (_records is [])
			return null;

		var numLogRecordsUnframed = _records.Count;
		await _handler(_records, token);
		_records.Clear();
		return numLogRecordsUnframed;
	}
}
