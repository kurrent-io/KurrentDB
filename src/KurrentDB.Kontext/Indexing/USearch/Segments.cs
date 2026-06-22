// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json;
using System.Text.Json.Serialization;

namespace KurrentDB.Kontext.Indexing.USearch;

// The immutable on-disk segment set, organised into levels. Owns the reader/writer lock guarding
// the list and the manifest that records it — so segments are never read while being swapped, and
// the on-disk manifest always matches the in-memory set. Searches share the read lock (run in
// parallel); only Add/Replace/Dispose take the write lock.
sealed class Segments : IDisposable {
	const int MaxSegmentsPerLevel = 4;

	readonly ReaderWriterLockSlim _lock = new();
	readonly List<Segment> _segments = [];
	readonly string _manifestPath;
	bool _disposed;

	public Segments(string directory, string manifestPath) {
		_manifestPath = manifestPath;

		if (File.Exists(manifestPath))
			foreach (var entry in LoadManifest(manifestPath))
				_segments.Add(new Segment(Path.Combine(directory, entry.File), entry.Level));
	}

	public int Count {
		get {
			_lock.EnterReadLock();
			try { return _segments.Count; } finally { _lock.ExitReadLock(); }
		}
	}

	public long TotalVectors {
		get {
			_lock.EnterReadLock();
			try { return _segments.Sum(s => (long)s.Size); } finally { _lock.ExitReadLock(); }
		}
	}

	public bool Contains(ulong id) {
		_lock.EnterReadLock();
		try {
			foreach (var segment in _segments)
				if (segment.Contains(id))
					return true;
			return false;
		} finally {
			_lock.ExitReadLock();
		}
	}

	public bool TryGet(ulong id, out ReadOnlyMemory<float> vector) {
		_lock.EnterReadLock();
		try {
			foreach (var segment in _segments)
				if (segment.TryGet(id, out vector))
					return true;
		} finally {
			_lock.ExitReadLock();
		}
		vector = ReadOnlyMemory<float>.Empty;
		return false;
	}

	// Searches each segment and offers its nearest hits to matches.
	public void Search(ReadOnlyMemory<float> query, int limit, NearestMatches matches) {
		_lock.EnterReadLock();
		try {
			foreach (var segment in _segments) {
				var size = int.CreateSaturating(segment.Size);
				if (size == 0)
					continue;
				var hits = segment.Search(query, Math.Min(limit, size), out var keys, out var distances);
				for (var j = 0; j < hits; j++)
					matches.Offer(keys.Span[j], distances.Span[j]);
			}
		} finally {
			_lock.ExitReadLock();
		}
	}

	public void Add(Segment segment) {
		_lock.EnterWriteLock();
		try {
			if (_disposed) {
				segment.Dispose();
				return;
			}
			_segments.Add(segment);
			SaveManifest();
		} finally {
			_lock.ExitWriteLock();
		}
	}

	// A level that holds too many segments and should be merged up, or null if none.
	public (List<Segment> Group, int TargetLevel)? FindMergeGroup() {
		_lock.EnterReadLock();
		try {
			var overflowing = _segments
				.GroupBy(s => s.Level)
				.OrderBy(g => g.Key)
				.FirstOrDefault(g => g.Count() > MaxSegmentsPerLevel);
			return overflowing is null ? null : (overflowing.ToList(), overflowing.Key + 1);
		} finally {
			_lock.ExitReadLock();
		}
	}

	// Atomically swaps a merged segment in for the group it was built from, then deletes the
	// originals. Disposal is safe here because the write lock excludes all searches.
	public void Replace(List<Segment> group, Segment merged) {
		_lock.EnterWriteLock();
		try {
			if (_disposed) {
				merged.Dispose();
				return;
			}
			foreach (var segment in group)
				_segments.Remove(segment);
			_segments.Add(merged);
			SaveManifest();
			foreach (var segment in group)
				segment.DisposeAndDelete();
		} finally {
			_lock.ExitWriteLock();
		}
	}

	void SaveManifest() {
		var manifest = new Manifest(_segments
			.Select(s => new ManifestEntry(Path.GetFileName(s.Path), s.Level))
			.ToArray());
		var tmp = _manifestPath + ".tmp";
		File.WriteAllText(tmp, JsonSerializer.Serialize(manifest));
		File.Move(tmp, _manifestPath, overwrite: true);
	}

	static IReadOnlyList<ManifestEntry> LoadManifest(string path) =>
		JsonSerializer.Deserialize<Manifest>(File.ReadAllText(path))?.Segments ?? [];

	public void Dispose() {
		_lock.EnterWriteLock();
		try {
			_disposed = true;
			foreach (var segment in _segments)
				segment.Dispose();
			_segments.Clear();
		} finally {
			_lock.ExitWriteLock();
		}
		// Deliberately not disposing _lock: a query racing shutdown may still call EnterReadLock, and
		// disposing it would throw. The lock is cheap and GC-reclaimed; late reads just see the empty
		// set and return nothing.
	}

	record Manifest([property: JsonPropertyName("segments")] ManifestEntry[] Segments);

	record ManifestEntry(
		[property: JsonPropertyName("file")] string File,
		[property: JsonPropertyName("level")] int Level);
}
