// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using EventStore.Core.Data;

namespace EventStore.Core.TransactionLog.Scavenging.Interfaces;

public interface IOriginalStreamScavengeMap<TKey> :
	IScavengeMap<TKey, OriginalStreamData> {

	IEnumerable<KeyValuePair<TKey, OriginalStreamData>> ActiveRecords();

	IEnumerable<KeyValuePair<TKey, OriginalStreamData>> ActiveRecordsFromCheckpoint(TKey checkpoint);

	void SetTombstone(TKey key);

	void SetMetadata(TKey key, StreamMetadata metadata);

	void SetDiscardPoints(
		TKey key,
		CalculationStatus status,
		DiscardPoint discardPoint,
		DiscardPoint maybeDiscardPoint);

	bool TryGetChunkExecutionInfo(TKey key, out ChunkExecutionInfo info);

	void DeleteMany(bool deleteArchived);
}
