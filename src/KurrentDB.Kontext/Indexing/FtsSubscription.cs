// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text;
using KurrentDB.Core;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Kontext.Diagnostics;
using KurrentDB.Kontext.Events;
using KurrentDB.Kontext.Search;
using KurrentDB.Kontext.Workspaces;
using KurrentDB.Kontext.Workspaces.Registry;
using Microsoft.Extensions.Logging;

namespace KurrentDB.Kontext.Indexing;

public sealed class FtsSubscription : SubscriptionBase {
	const int BatchSize = 100;
	const int StreamLruCapacity = 4096;

	readonly IFtsStore _eventsStore;
	readonly IFtsStore _memoryStore;
	readonly IFtsStore _eventsStreamsStore;
	readonly IFtsStore _memoryStreamsStore;
	readonly StreamHashLru _eventsStreamsLru = new(StreamLruCapacity);
	readonly StreamHashLru _memoryStreamsLru = new(StreamLruCapacity);

	readonly NounPhraseExtractor _npe;
	readonly IndexingStatus _status;
	readonly KontextProgressTracker _tracker;
	readonly ILogger<FtsSubscription> _logger;

	readonly ulong[] _preparePositions = new ulong[BatchSize];
	readonly TFPos[] _positions = new TFPos[BatchSize];
	readonly string[] _keys = new string[BatchSize];
	readonly string[] _values = new string[BatchSize];
	readonly bool[] _toMemory = new bool[BatchSize];
	int _count;

	readonly StringBuilder _keysBuilder = new();
	readonly StringBuilder _valuesBuilder = new();
	readonly StringBuilder _streamNameBuilder = new();

	public FtsSubscription(
		ISystemClient client,
		WorkspaceEntry workspace,
		IEventFilter filter,
		string checkpointPath,
		IFtsStore eventsStore,
		IFtsStore memoryStore,
		IFtsStore eventsStreamsStore,
		IFtsStore memoryStreamsStore,
		NounPhraseExtractor npe,
		IndexingStatus status,
		KontextProgressTracker tracker,
		ILogger<FtsSubscription> logger)
		: base(client, workspace, filter, checkpointPath) {
		_eventsStore = eventsStore;
		_memoryStore = memoryStore;
		_eventsStreamsStore = eventsStreamsStore;
		_memoryStreamsStore = memoryStreamsStore;
		_npe = npe;
		_status = status;
		_tracker = tracker;
		_logger = logger;
	}

	protected override string PipelineName => "Full-Text Indexing";
	protected override ILogger Logger => _logger;
	protected override void OnCaughtUp() => _status.RecordFtsCaughtUp();
	protected override void UpdatePosition(ulong commit) => _status.UpdateFtsPosition(commit);
	protected override void RecordIndexed(DateTime ts) => _status.RecordFtsIndexed(ts);
	protected override KontextProgressTracker.Scope StartIndexScope() => _tracker.StartFtsIndex();
	protected override KontextProgressTracker.Scope StartCommitScope() => _tracker.StartFtsCommit();

	protected override TFPos FlushIndexes(TFPos current, bool disposing) {
		var watermark = FlushStore(_eventsStore, current);
		watermark = Min(watermark, FlushStore(_memoryStore, current));
		watermark = Min(watermark, FlushStore(_eventsStreamsStore, current));
		watermark = Min(watermark, FlushStore(_memoryStreamsStore, current));
		return watermark;
	}

	protected override int BatchCount => _count;
	protected override bool IsBatchFull => _count >= BatchSize;

	protected override ValueTask StageEventAsync(
		ResolvedEvent evt, IndexKind targetKind, CancellationToken ct) {
		var record = evt.OriginalEvent;
		var (keys, values) = EventTextHelpers.BuildFlatKeyValueTexts(record.EventType, record.Data.Span, _keysBuilder, _valuesBuilder);

		var position = evt.OriginalPosition!.Value;
		_preparePositions[_count] = (ulong)position.PreparePosition;
		_positions[_count] = position;
		_keys[_count] = keys;
		_values[_count] = _npe.ExtractText(values);
		_toMemory[_count] = targetKind == IndexKind.Memory;
		_count++;

		IndexStreamName(record.EventStreamId, targetKind, position);
		return ValueTask.CompletedTask;
	}

	void IndexStreamName(string streamName, IndexKind targetKind, TFPos position) {
		var hash = StreamHasher.Hash(streamName);
		var (lru, store) = targetKind == IndexKind.Memory
			? (_memoryStreamsLru, _memoryStreamsStore)
			: (_eventsStreamsLru, _eventsStreamsStore);
		if (!lru.TryAdd(hash))
			return;
		_streamNameBuilder.Clear();
		EventTextHelpers.AppendSplitName(streamName, _streamNameBuilder);
		store.Add(hash, position, keys: string.Empty, values: _streamNameBuilder.ToString(), forceUpdate: true);
	}

	protected override Task IndexBatchAsync(CancellationToken ct) {
		var count = _count;
		var eventsTouched = false;
		var memoryTouched = false;

		for (var i = 0; i < count; i++) {
			var store = _toMemory[i] ? _memoryStore : _eventsStore;
			store.Add(_preparePositions[i], _positions[i], _keys[i], _values[i]);
			if (_toMemory[i])
				memoryTouched = true;
			else
				eventsTouched = true;
		}
		if (eventsTouched) { _eventsStore.Refresh(); _eventsStreamsStore.Refresh(); }
		if (memoryTouched) { _memoryStore.Refresh(); _memoryStreamsStore.Refresh(); }

		Array.Clear(_keys, 0, count);
		Array.Clear(_values, 0, count);
		_count = 0;
		return Task.CompletedTask;
	}
}