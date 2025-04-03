// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using EventStore.Core.Data;
using EventStore.Core.TransactionLog.Scavenging;
using EventStore.Core.TransactionLog.Scavenging.Interfaces;

namespace EventStore.Core.XUnit.Tests.Scavenge.Infrastructure;

public class TracingOriginalStreamScavengeMap<TKey> : IOriginalStreamScavengeMap<TKey> {
	private readonly IOriginalStreamScavengeMap<TKey> _wrapped;
	private readonly Tracer _tracer;

	public TracingOriginalStreamScavengeMap(
		IOriginalStreamScavengeMap<TKey> wrapped,
		Tracer tracer) {

		_wrapped = wrapped;
		_tracer = tracer;
	}

	public OriginalStreamData this[TKey key] { set => _wrapped[key] = value; }

	public void DeleteMany(bool deleteArchived) {
		_wrapped.DeleteMany(deleteArchived);
	}

	public IEnumerable<KeyValuePair<TKey, OriginalStreamData>> AllRecords() =>
		_wrapped.AllRecords();

	public IEnumerable<KeyValuePair<TKey, OriginalStreamData>> ActiveRecords() =>
		_wrapped.ActiveRecords();

	public IEnumerable<KeyValuePair<TKey, OriginalStreamData>> ActiveRecordsFromCheckpoint(TKey checkpoint) =>
		_wrapped.ActiveRecordsFromCheckpoint(checkpoint);

	public void SetDiscardPoints(
		TKey key,
		CalculationStatus status,
		DiscardPoint discardPoint,
		DiscardPoint maybeDiscardPoint) {

		_tracer.Trace($"SetDiscardPoints({key}, {status}, {discardPoint}, {maybeDiscardPoint})");
		_wrapped.SetDiscardPoints(key, status, discardPoint, maybeDiscardPoint);
	}

	public void SetMetadata(TKey key, StreamMetadata metadata) {
		_wrapped.SetMetadata(key, metadata);
	}

	public void SetTombstone(TKey key) {
		_wrapped.SetTombstone(key);
	}

	public bool TryGetChunkExecutionInfo(TKey key, out ChunkExecutionInfo details) {
		return _wrapped.TryGetChunkExecutionInfo(key, out details);
	}

	public bool TryGetValue(TKey key, out OriginalStreamData value) {
		return _wrapped.TryGetValue(key, out value);
	}

	public bool TryRemove(TKey key, out OriginalStreamData value) {
		return _wrapped.TryRemove(key, out value);
	}
}
