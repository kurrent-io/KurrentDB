// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Data;
using Microsoft.Extensions.Logging;

namespace KurrentDB.Kontext.Indexing.USearch;

/// <summary>
/// An LSM-tree over USearch indexes: a mutable in-memory L0 buffer (brute-force scanned) that seals
/// into immutable on-disk HNSW segments, which a background thread merges up by level.
///
/// Thread safety:
/// - Add, Flush: single writer only (the indexing subscription). They mutate the L0 buffer and swap
///   it without locking; concurrent writers would corrupt it.
/// - Search, TryGet: safe on any thread, concurrently with the writer. The L0 read is lock-free (a
///   volatile snapshot); segment reads take a shared read lock.
/// - Dispose: call once, after writes have stopped. A query racing disposal degrades to no results
///   rather than throwing.
/// </summary>
public class USearchVectorStore : IVectorStore {
	const int DefaultSealThreshold = 1_000;

	readonly ILogger<USearchVectorStore> _logger;

	readonly string _name;
	readonly string _directory;
	readonly VectorIndexMetadata _meta;
	readonly int _sealThreshold;

	readonly Segments _segments;
	readonly SegmentMerger _merger;
	volatile ActiveSnapshot _active;

	bool _inRecovery;

	TFPos _lastAdded;
	TFPos _durable;

	public USearchVectorStore(string path, string index, VectorIndexMetadata meta,
		ILogger<USearchVectorStore> logger, int sealThreshold = DefaultSealThreshold) {
		_name = index;
		_meta = meta;
		_sealThreshold = sealThreshold;
		_logger = logger;

		_directory = Path.Combine(path, $"{index}.usearch");
		Directory.CreateDirectory(_directory);

		var manifestPath = Path.Combine(_directory, "manifest.usearchmap");
		var metaPath = Path.Combine(_directory, "meta.json");
		var existed = File.Exists(manifestPath);

		if (existed) {
			var stored = VectorIndexMetadata.Load(metaPath);
			if (stored != _meta) {
				throw new InvalidOperationException(
					$"Vector index '{index}' was built with {Describe(stored)}, " +
					$"but the configured embedding generator produces {Describe(_meta)}. " +
					$"Reindex required: delete the contents of " +
					$"'{_directory}' and the corresponding checkpoint.embeddings file, then restart.");

				static string Describe(VectorIndexMetadata meta) =>
					$"{meta.Provider}/{meta.Model ?? "default"} ({meta.Dimensions} dims)";
			}
		} else {
			_meta.Save(metaPath);
		}

		_segments = new Segments(_directory, manifestPath);
		_merger = new SegmentMerger(_segments, _directory, logger, index);

		if (existed)
			_logger.LogDebug("Loaded USearch index {Index}: {Segments} segment(s), {Size} vectors on disk",
				index, _segments.Count, _segments.TotalVectors);
		else
			_logger.LogDebug("Created new USearch index {Index} (model={Model}, dims={Dimensions})",
				index, _meta.Model, _meta.Dimensions);

		_active = new ActiveSnapshot(_sealThreshold);
		_inRecovery = true;
	}

	public void Add(ulong id, TFPos position, ReadOnlyMemory<float> vector) {
		_lastAdded = position;

		if (_inRecovery) {
			if (_segments.Contains(id))
				return;
			_inRecovery = false;
			_logger.LogDebug("USearch index {Index} exited recovery probe", _name);
		}

		if (_active.Append(id, vector) >= _sealThreshold)
			Seal();
	}

	public IEnumerable<ulong> Search(ReadOnlyMemory<float> query, int limit) {
		if (limit <= 0)
			return [];

		var matches = new NearestMatches();
		_active.Search(query.Span, matches);
		_segments.Search(query, limit, matches);

		return matches.Nearest(limit);
	}

	public bool TryGet(ulong id, out ReadOnlyMemory<float> vector) {
		if (_active.TryGet(id, out vector))
			return true;

		if (_segments.TryGet(id, out var vec)) {
			vector = vec;
			return true;
		}

		vector = default;
		return false;
	}

	public TFPos Flush(TFPos current, bool force) {
		if (force)
			Seal();

		if (_active.Count == 0) {
			_durable = current;
			return current;
		}

		return _durable;
	}

	void Seal() {
		if (_active.Count == 0)
			return;

		var (ids, vecs) = _active.Extract();
		var segment = Segment.Build(_directory, _meta.Dimensions, ids, vecs, level: 0);

		// Add the segment before resetting the buffer, so a concurrent search never sees the
		// just-sealed ids in neither tier.
		_segments.Add(segment);
		_active = new ActiveSnapshot(_sealThreshold);
		_durable = _lastAdded;

		_logger.LogDebug("Sealed USearch L0 segment {Index}: {Count} vectors", _name, ids.Length);
		_merger.Trigger();
	}

	public void Dispose() {
		// Stop merges before disposing the segment set they operate on.
		_merger.Dispose();
		_segments.Dispose();
	}
}
