// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using KurrentDB.Core.TransactionLog.LogRecords;

namespace KurrentDB.Core.Services.Storage;

/// <summary>
/// Given all the prepares for an implicit transaction, calculates the number of streams
/// in the transaction and numbers them so they can be used as indexes
/// </summary>
public class ImplicitTransactionCalculator<TStreamId> {
	private readonly Dictionary<TStreamId, int> _streamIndexes = [];
	private readonly List<int> _eventStreamIndexes = [];

	public int NumStreamsInTransaction => _streamIndexes.Count;

	public ReadOnlySpan<int> EventStreamIndexes =>
		CollectionsMarshal.AsSpan(_eventStreamIndexes);

	public void SetPrepares(IReadOnlyList<IPrepareLogRecord<TStreamId>> prepares) {
		_streamIndexes.Clear();
		_eventStreamIndexes.Clear();

		foreach (var prepare in prepares) {
			if (!_streamIndexes.TryGetValue(prepare.EventStreamId, out var streamIndex)) {
				streamIndex = _streamIndexes.Count;
				_streamIndexes[prepare.EventStreamId] = streamIndex;
			}

			_eventStreamIndexes.Add(streamIndex);
		}
	}
}
