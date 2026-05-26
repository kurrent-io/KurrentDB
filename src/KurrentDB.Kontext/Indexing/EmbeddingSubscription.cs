// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text;
using KurrentDB.Core;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Kontext.Diagnostics;
using KurrentDB.Kontext.Embeddings;
using KurrentDB.Kontext.Events;
using KurrentDB.Kontext.Workspaces;
using KurrentDB.Kontext.Workspaces.Registry;
using KurrentDB.Kontext.Workspaces.Runtime;
using Microsoft.Extensions.Logging;

namespace KurrentDB.Kontext.Indexing;

public sealed class EmbeddingSubscription : SubscriptionBase {
	const int StreamLruCapacity = 4096;

	readonly IVectorStore _eventsStore;
	readonly IVectorStore _memoryStore;
	readonly IVectorStore _eventsStreamsStore;
	readonly IVectorStore _memoryStreamsStore;
	readonly StreamHashLru _eventsStreamsLru = new(StreamLruCapacity);
	readonly StreamHashLru _memoryStreamsLru = new(StreamLruCapacity);

	readonly EmbeddingCache _cache;
	readonly WorkspaceBloomFilters _filters;
	readonly IndexingStatus _status;
	readonly KontextProgressTracker _tracker;
	readonly ILogger<EmbeddingSubscription> _logger;

	readonly int _batchSize;

	// Event batch
	readonly ulong[] _eventPositions;
	readonly TFPos[] _eventTfPositions;
	readonly string[] _eventStreams;
	readonly string[] _eventTexts;
	readonly IndexKind[] _eventKinds;
	readonly ReadOnlyMemory<float>[] _eventEmbeddings;
	int _eventCount;

	// Stream-name batch
	readonly ulong[] _streamHashes;
	readonly TFPos[] _streamTfPositions;
	readonly string[] _streamNames;
	readonly string[] _streamTexts;
	readonly IndexKind[] _streamKinds;
	readonly ReadOnlyMemory<float>[] _streamEmbeddings;
	int _streamCount;

	readonly StringBuilder _textBuilder = new();
	readonly StringBuilder _streamNameBuilder = new();

	public EmbeddingSubscription(
		ISystemClient client,
		WorkspaceEntry workspace,
		IEventFilter filter,
		string checkpointPath,
		IVectorStore eventsStore,
		IVectorStore memoryStore,
		IVectorStore eventsStreamsStore,
		IVectorStore memoryStreamsStore,
		EmbeddingCache cache,
		WorkspaceBloomFilters filters,
		KontextEmbeddingsConfig embeddingsConfig,
		IndexingStatus status,
		KontextProgressTracker tracker,
		ILogger<EmbeddingSubscription> logger)
		: base(client, workspace, filter, checkpointPath) {
		_eventsStore = eventsStore;
		_memoryStore = memoryStore;
		_eventsStreamsStore = eventsStreamsStore;
		_memoryStreamsStore = memoryStreamsStore;
		_cache = cache;
		_filters = filters;
		_status = status;
		_tracker = tracker;
		_logger = logger;

		_batchSize = embeddingsConfig.BatchSize;
		_eventPositions = new ulong[_batchSize];
		_eventTfPositions = new TFPos[_batchSize];
		_eventStreams = new string[_batchSize];
		_eventTexts = new string[_batchSize];
		_eventKinds = new IndexKind[_batchSize];
		_eventEmbeddings = new ReadOnlyMemory<float>[_batchSize];
		_streamHashes = new ulong[_batchSize];
		_streamTfPositions = new TFPos[_batchSize];
		_streamNames = new string[_batchSize];
		_streamTexts = new string[_batchSize];
		_streamKinds = new IndexKind[_batchSize];
		_streamEmbeddings = new ReadOnlyMemory<float>[_batchSize];
	}

	protected override string PipelineName => "Semantic Indexing";
	protected override ILogger Logger => _logger;
	protected override void OnCaughtUp() => _status.RecordEmbeddingCaughtUp();
	protected override void UpdatePosition(ulong commit) => _status.UpdateEmbeddingPosition(commit);
	protected override void RecordIndexed(DateTime ts) => _status.RecordEmbeddingIndexed(ts);
	protected override KontextProgressTracker.Scope StartIndexScope() => _tracker.StartEmbeddingIndex();
	protected override KontextProgressTracker.Scope StartCommitScope() => _tracker.StartEmbeddingCommit();

	protected override TFPos FlushIndexes(TFPos current, bool disposing) {
		var watermark = FlushStore(_eventsStore, current);
		watermark = Min(watermark, FlushStore(_memoryStore, current));
		watermark = Min(watermark, FlushStore(_eventsStreamsStore, current));
		watermark = Min(watermark, FlushStore(_memoryStreamsStore, current));
		_filters.Flush(throttle: !disposing);
		return watermark;
	}

	protected override int BatchCount => _eventCount;
	protected override bool IsBatchFull => _eventCount >= _batchSize;

	protected override ValueTask StageEventAsync(
		ResolvedEvent evt, IndexKind targetKind, CancellationToken ct) {
		var record = evt.OriginalEvent;
		var position = evt.OriginalPosition!.Value;
		_eventPositions[_eventCount] = (ulong)position.PreparePosition;
		_eventTfPositions[_eventCount] = position;
		_eventStreams[_eventCount] = record.EventStreamId;
		_eventTexts[_eventCount] = EventTextHelpers.BuildEventText(record.EventType, record.Data.Span, _textBuilder);
		_eventKinds[_eventCount] = targetKind;
		_eventCount++;

		StageStreamName(record.EventStreamId, targetKind, position);
		return ValueTask.CompletedTask;
	}

	void StageStreamName(string streamName, IndexKind targetKind, TFPos position) {
		var hash = StreamHasher.Hash(streamName);
		var lru = targetKind == IndexKind.Memory ? _memoryStreamsLru : _eventsStreamsLru;
		if (!lru.TryAdd(hash))
			return;

		_streamNameBuilder.Clear();
		EventTextHelpers.AppendSplitName(streamName, _streamNameBuilder);
		_streamHashes[_streamCount] = hash;
		_streamTfPositions[_streamCount] = position;
		_streamNames[_streamCount] = streamName;
		_streamTexts[_streamCount] = _streamNameBuilder.ToString();
		_streamKinds[_streamCount] = targetKind == IndexKind.Memory ? IndexKind.MemoryStreams : IndexKind.EventsStreams;
		_streamCount++;
	}

	protected override async Task IndexBatchAsync(CancellationToken ct) {
		var count = _eventCount;

		await _cache.GetOrCreateEventEmbeddingsAsync(
			Workspace.Name, _eventPositions.AsMemory(0, count), _eventStreams.AsMemory(0, count),
			_eventKinds.AsMemory(0, count), _eventTexts.AsMemory(0, count), _eventEmbeddings.AsMemory(0, count), ct);

		for (var i = 0; i < count; i++) {
			var store = _eventKinds[i] == IndexKind.Memory ? _memoryStore : _eventsStore;
			store.Add(_eventPositions[i], _eventTfPositions[i], _eventEmbeddings[i]);
			_filters.AddLogPosition(_eventPositions[i]);
		}

		if (_streamCount > 0) {
			var streamCount = _streamCount;
			await _cache.GetOrCreateStreamEmbeddingsAsync(
				Workspace.Name, _streamHashes.AsMemory(0, streamCount), _streamNames.AsMemory(0, streamCount),
				_streamKinds.AsMemory(0, streamCount), _streamTexts.AsMemory(0, streamCount), _streamEmbeddings.AsMemory(0, streamCount), ct);

			for (var i = 0; i < streamCount; i++) {
				var store = _streamKinds[i] == IndexKind.MemoryStreams ? _memoryStreamsStore : _eventsStreamsStore;
				store.Add(_streamHashes[i], _streamTfPositions[i], _streamEmbeddings[i]);
				_filters.AddStreamHash(_streamHashes[i]);
			}

			Array.Clear(_streamNames, 0, streamCount);
			Array.Clear(_streamTexts, 0, streamCount);
			Array.Clear(_streamEmbeddings, 0, streamCount);
			_streamCount = 0;
		}

		Array.Clear(_eventStreams, 0, count);
		Array.Clear(_eventTexts, 0, count);
		Array.Clear(_eventEmbeddings, 0, count);
		_eventCount = 0;
	}
}
