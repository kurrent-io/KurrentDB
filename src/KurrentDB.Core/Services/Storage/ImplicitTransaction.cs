// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using KurrentDB.Common.Utils;
using KurrentDB.Core.TransactionLog.LogRecords;

namespace KurrentDB.Core.Services.Storage;

public class ImplicitTransaction<TStreamId> {
	private readonly List<IPrepareLogRecord<TStreamId>> _prepares = [];
	private readonly Dictionary<TStreamId, int> _streamIndexes = [];
	private int CurrentStreamIndex => _firstEventNumbers.Count;
	private readonly List<long> _firstEventNumbers = [];
	private readonly List<long> _lastEventNumbers = [];
	private readonly List<int> _eventStreamIndexes = [];

	public long? Position { get; private set; }
	public LowAllocReadOnlyMemory<long> GetFirstEventNumbers() => _firstEventNumbers.ToLowAllocReadOnlyMemory();
	public LowAllocReadOnlyMemory<long> GetLastEventNumbers() => _lastEventNumbers.ToLowAllocReadOnlyMemory();
	public LowAllocReadOnlyMemory<int> GetEventStreamIndexes() {
		if (CurrentStreamIndex == 1)
			return [];

		return _eventStreamIndexes.ToLowAllocReadOnlyMemory();
	}

	public IReadOnlyList<IPrepareLogRecord<TStreamId>> Prepares => _prepares;

	public void Process(IPrepareLogRecord<TStreamId> prepare) {
		Position = prepare.TransactionPosition;

		if (!_streamIndexes.TryGetValue(prepare.EventStreamId, out var streamIndex)) {
			streamIndex = CurrentStreamIndex;
			_streamIndexes[prepare.EventStreamId] = streamIndex;
			_firstEventNumbers.Add(prepare.ExpectedVersion + 1);
			_lastEventNumbers.Add(prepare.ExpectedVersion);
		}

		if (!prepare.Flags.HasAnyOf(PrepareFlags.Data | PrepareFlags.StreamDelete))
			return;

		_prepares.Add(prepare);
		_eventStreamIndexes.Add(streamIndex);

		if (!prepare.Flags.HasAnyOf(PrepareFlags.Data))
			return;

		if (_lastEventNumbers[streamIndex] != prepare.ExpectedVersion)
			throw new ArgumentOutOfRangeException(nameof(prepare),
				$"Expected prepare to have {nameof(prepare.ExpectedVersion)}: {_lastEventNumbers[streamIndex]} but was {prepare.ExpectedVersion}");
		_lastEventNumbers[streamIndex] = prepare.ExpectedVersion + 1;
	}

	public void Clear() {
		Position = null;
		_prepares.Clear();
		_streamIndexes.Clear();
		_firstEventNumbers.Clear();
		_lastEventNumbers.Clear();
		_eventStreamIndexes.Clear();
	}
}
