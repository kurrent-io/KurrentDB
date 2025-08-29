// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Core.Data;
using static KurrentDB.Core.Messages.ClientMessage;

// ReSharper disable LoopCanBeConvertedToQuery

namespace KurrentDB.Core.Services.Storage;

public interface ISecondaryIndexReader {
	bool CanReadIndex(string indexName);

	TFPos GetLastIndexedPosition(string indexName);

	ValueTask<ReadIndexEventsForwardCompleted> ReadForwards(ReadIndexEventsForward msg, CancellationToken token);

	ValueTask<ReadIndexEventsBackwardCompleted> ReadBackwards(ReadIndexEventsBackward msg, CancellationToken token);
}

public class SecondaryIndexReaders {
	ISecondaryIndexReader[] _readers = [];

	public void AddReaders(IEnumerable<ISecondaryIndexReader> readers) {
		_readers = readers.ToArray();
	}

	public bool CanReadIndex(string indexName) => _readers.Any(r => r.CanReadIndex(indexName));

	public ValueTask<ReadIndexEventsForwardCompleted> ReadForwards(ReadIndexEventsForward msg, CancellationToken token) {
		var reader = FindReader(msg.IndexName);

		return reader?.ReadForwards(msg, token) ?? ValueTask.FromResult(
			new ReadIndexEventsForwardCompleted(
				ReadIndexResult.IndexNotFound, [], new(msg.CommitPosition, msg.PreparePosition), -1, true,
				$"Index {msg.IndexName} does not exist"
			));
	}

	public ValueTask<ReadIndexEventsBackwardCompleted> ReadBackwards(ReadIndexEventsBackward msg, CancellationToken token) {
		var reader = FindReader(msg.IndexName);

		return reader?.ReadBackwards(msg, token) ?? ValueTask.FromResult(new ReadIndexEventsBackwardCompleted(
			ReadIndexResult.IndexNotFound, [], -1, true,
			$"Index {msg.IndexName} does not exist"
		));
	}

	public TFPos GetLastIndexedPosition(string indexName) {
		var reader = FindReader(indexName);
		ArgumentNullException.ThrowIfNull(reader, nameof(reader));

		return reader.GetLastIndexedPosition(indexName);
	}

	[CanBeNull]
	ISecondaryIndexReader FindReader(string indexName) {
		for (var i = 0; i < _readers.Length; i++) {
			var reader = _readers[i];
			if (reader.CanReadIndex(indexName))
				return reader;
		}

		return null;
	}
}
