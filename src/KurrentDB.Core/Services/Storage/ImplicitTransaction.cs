// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using KurrentDB.Common.Utils;
using KurrentDB.Core.TransactionLog.LogRecords;

namespace KurrentDB.Core.Services.Storage;

public class ImplicitTransaction<TStreamId> {
	private readonly List<IPrepareLogRecord<TStreamId>> _prepares = [];
	private readonly Dictionary<TStreamId, int> _streamIndexes = [];
	private readonly List<int> _eventStreamIndexes = [];

	public int NumStreams => _streamIndexes.Count;
	public long? Position { get; private set; }
	public LowAllocReadOnlyMemory<int> GetEventStreamIndexes() {
		if (NumStreams == 1)
			return [];

		return _eventStreamIndexes.ToLowAllocReadOnlyMemory();
	}

	public IReadOnlyList<IPrepareLogRecord<TStreamId>> Prepares => _prepares;

	public void Process(IPrepareLogRecord<TStreamId> prepare) {
		Position = prepare.TransactionPosition;

		if (!_streamIndexes.TryGetValue(prepare.EventStreamId, out var streamIndex)) {
			streamIndex = NumStreams;
			_streamIndexes[prepare.EventStreamId] = streamIndex;
		}

		if (!prepare.Flags.HasAnyOf(PrepareFlags.Data | PrepareFlags.StreamDelete))
			return;

		_prepares.Add(prepare);
		_eventStreamIndexes.Add(streamIndex);
	}

	public void Clear() {
		Position = null;
		_prepares.Clear();
		_streamIndexes.Clear();
		_eventStreamIndexes.Clear();
	}
}
