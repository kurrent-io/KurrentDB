// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Core.Data;
using static KurrentDB.Core.Messages.ClientMessage;

// ReSharper disable LoopCanBeConvertedToQuery

namespace KurrentDB.Core.Services.Storage;

public enum IndexSubscriptionResult {
	NotFound,
	InvalidConstraints,
	Ok,
}

public interface ISecondaryIndexReader {
	bool CanReadIndex(string indexName);

	TFPos GetLastIndexedPosition(string indexName);

	ValueTask<ReadIndexEventsForwardCompleted> ReadForwards(ReadIndexEventsForward msg, CancellationToken token);

	ValueTask<ReadIndexEventsBackwardCompleted> ReadBackwards(ReadIndexEventsBackward msg, CancellationToken token);

	IndexSubscriptionResult TryParseIndexSubscription(string indexStream, out string indexKey, out IReadOnlyList<KeyValuePair<string, string>> constraints) {
		indexKey = indexStream;
		constraints = [];
		return IndexSubscriptionResult.Ok;
	}
}

public class SecondaryIndexReaders {
	ISecondaryIndexReader[] _readers = [];

	public SecondaryIndexReaders AddReaders(IEnumerable<ISecondaryIndexReader> readers) {
		_readers = readers.ToArray();
		return this;
	}

	public ValueTask<ReadIndexEventsForwardCompleted> ReadForwards(ReadIndexEventsForward msg, CancellationToken token) =>
		TryGetReader(msg.IndexName, out var reader)
			? reader.ReadForwards(msg, token)
			: ValueTask.FromResult(new ReadIndexEventsForwardCompleted(
				ReadIndexResult.IndexNotFound, [], new(msg.CommitPosition, msg.PreparePosition), -1, true,
				$"Index {msg.IndexName} does not exist"));

	public ValueTask<ReadIndexEventsBackwardCompleted> ReadBackwards(ReadIndexEventsBackward msg, CancellationToken token) =>
		TryGetReader(msg.IndexName, out var reader)
			? reader.ReadBackwards(msg, token)
			: ValueTask.FromResult(new ReadIndexEventsBackwardCompleted(
				ReadIndexResult.IndexNotFound, [], new(msg.CommitPosition, msg.PreparePosition), -1, true,
				$"Index {msg.IndexName} does not exist"));

	public TFPos GetLastIndexedPosition(string indexName) =>
		TryGetReader(indexName, out var reader) ? reader.GetLastIndexedPosition(indexName) : TFPos.Invalid;

	public IndexSubscriptionResult TryParseIndexSubscription(string indexStream, out string indexKey, out IReadOnlyList<KeyValuePair<string, string>> constraints) {
		if (TryGetReader(indexStream, out var reader))
			return reader.TryParseIndexSubscription(indexStream, out indexKey, out constraints);

		indexKey = string.Empty;
		constraints = [];
		return IndexSubscriptionResult.NotFound;
	}

	private bool TryGetReader(string indexName, out ISecondaryIndexReader reader) {
		foreach (var r in _readers) {
			if (r.CanReadIndex(indexName)) {
				reader = r;
				return true;
			}
		}

		reader = null;
		return false;
	}
}
