// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using DotNext;
using Kurrent.Quack;
using KurrentDB.Core.Data;
using KurrentDB.Common.Log;
using Serilog;

namespace KurrentDB.SecondaryIndexing.Indices.Default;

internal class DefaultSecondaryIndexProcessor<TStreamId> : Disposable, ISecondaryIndexProcessor {
	private readonly DefaultIndex<TStreamId> _defaultIndex;
	private long _lastLogPosition;
	private ulong _seq;
	private int _page;
	private readonly Appender _appender;

	private static readonly ILogger Logger = Log.Logger.ForContext(nameof(DefaultSecondaryIndexProcessor<TStreamId>));

	public long LastCommittedPosition { get; private set; }
	public long LastSequence => (long)_seq;

	public DefaultSecondaryIndexProcessor(
		DuckDBAdvancedConnection connection,
		DefaultIndex<TStreamId> defaultIndex
	) {
		_defaultIndex = defaultIndex;

		_appender = new Appender(connection, "idx_all"u8);

		var lastPosition = defaultIndex.GetLastSequence();
		Logger.Information("Last known global sequence: {Seq}", lastPosition);
		_seq = lastPosition.HasValue ? lastPosition.Value + 1 : 0;
	}

	public async ValueTask Index(ResolvedEvent resolvedEvent, CancellationToken token = default) {
		if (IsDisposingOrDisposed)
			return;

		await _defaultIndex.CategoryIndex.Processor.Index(resolvedEvent, token);
		await _defaultIndex.EventTypeIndex.Processor.Index(resolvedEvent, token);
		await _defaultIndex.StreamIndex.Processor.Index(resolvedEvent, token);

		var category = _defaultIndex.CategoryIndex.LastIndexed;
		var eventType = _defaultIndex.EventTypeIndex.LastIndexed;
		var streamId = _defaultIndex.StreamIndex.LastIndexed;

		using (var row = _appender.CreateRow()) {
			row.Append(_seq++);
			row.Append((int)resolvedEvent.Event.EventNumber);
			row.Append(resolvedEvent.Event.LogPosition);
			row.Append(resolvedEvent.Event.TimeStamp);
			row.Append(streamId);
			row.Append((int)eventType.Id);
			row.Append(eventType.Sequence);
			row.Append((int)category.Id);
			row.Append(category.Sequence);
		}

		_lastLogPosition = resolvedEvent.Event.LogPosition;
		_page++;
	}

	public async ValueTask Commit(CancellationToken token = default) {
		if (IsDisposingOrDisposed)
			return;

		_appender.Flush();
		Logger.Debug("Committed {Count} records to index at sequence {Seq}", _page, _seq);
		_page = 0;
		LastCommittedPosition = _lastLogPosition;

		await _defaultIndex.CategoryIndex.Processor.Commit(token);
		await _defaultIndex.EventTypeIndex.Processor.Commit(token);
		await _defaultIndex.StreamIndex.Processor.Commit(token);
	}
}
