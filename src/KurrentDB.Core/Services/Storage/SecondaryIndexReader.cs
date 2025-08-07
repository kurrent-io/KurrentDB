// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Core.Data;
using static KurrentDB.Core.Messages.ClientMessage;

namespace KurrentDB.Core.Services.Storage;

public interface ISecondaryIndexReader {
	bool CanReadIndex(string indexName);

	ValueTask<ReadIndexEventsForwardCompleted> ReadForwards(ReadIndexEventsForward msg, CancellationToken token);
}

public class SecondaryIndexReaders {
	ISecondaryIndexReader[] _readers = [];

	public void AddReaders(IEnumerable<ISecondaryIndexReader> readers) {
		_readers = readers.ToArray();
	}

	public ValueTask<ReadIndexEventsForwardCompleted> ReadForwards(ReadIndexEventsForward msg, CancellationToken token) {
		for (var i = 0; i < _readers.Length; i++) {
			var reader = _readers[i];
			if (!reader.CanReadIndex(msg.IndexName))
				continue;
			return reader.ReadForwards(msg, token);
		}

		return ValueTask.FromResult(new ReadIndexEventsForwardCompleted(
			msg.CorrelationId,
			ReadIndexResult.IndexNotFound,
			$"Index {msg.IndexName} does not exist",
			[],
			msg.MaxCount,
			new(msg.CommitPosition, msg.PreparePosition),
			TFPos.Invalid,
			TFPos.Invalid,
			-1,
			true
		));
	}
}
