// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using DotNext;
using Kurrent.Quack;
using KurrentDB.Core.Data;
using KurrentDB.Common.Log;
using Serilog;

namespace KurrentDB.SecondaryIndexing.Indices.Default;

public class DefaultSecondaryIndexProcessor<TStreamId> : Disposable, ISecondaryIndexProcessor {
	private long _lastLogPosition;
	private ulong _seq;
	private int _page;
	private readonly Appender _appender;

	private static readonly ILogger Logger = Log.Logger.ForContext(nameof(DefaultSecondaryIndexProcessor<TStreamId>));

	public long LastCommittedPosition { get; private set; }
	public long LastSequence => (long)_seq;

	public DefaultSecondaryIndexProcessor(DuckDBAdvancedConnection connection, DefaultIndex<TStreamId> defaultIndex) {
		_appender = new Appender(connection, "idx_all"u8);

		var lastPosition = defaultIndex.GetLastSequence();
		Logger.Information("Last known global sequence: {Seq}", lastPosition);
		_seq = lastPosition.HasValue ? lastPosition.Value + 1 : 0;
	}

	public ValueTask Index(ResolvedEvent resolvedEvent, CancellationToken token = default) {
		if (IsDisposingOrDisposed)
			return ValueTask.CompletedTask;

		using (var row = _appender.CreateRow()) {
			row.Append(_seq++);
			row.Append((int)resolvedEvent.Event.EventNumber);
			row.Append(resolvedEvent.Event.LogPosition);
			row.Append(resolvedEvent.Event.TimeStamp);
			row.Append(0);
			row.Append(0);
			row.Append(0);
			row.Append(0);
			row.Append(0);
		}

		_lastLogPosition = resolvedEvent.Event.LogPosition;
		_page++;

		return ValueTask.CompletedTask;
	}

	public ValueTask Commit(CancellationToken token = default) {
		if (IsDisposingOrDisposed)
			return ValueTask.CompletedTask;

		_appender.Flush();
		Logger.Debug("Committed {Count} records to index at sequence {Seq}", _page, _seq);
		_page = 0;
		LastCommittedPosition = _lastLogPosition;

		return ValueTask.CompletedTask;
	}
}
