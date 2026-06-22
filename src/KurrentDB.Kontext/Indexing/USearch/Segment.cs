// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Cloud.Unum.USearch;

namespace KurrentDB.Kontext.Indexing.USearch;

// An immutable, on-disk USearch (HNSW) segment plus its key sidecar. USearch exposes no key
// enumeration, so the sidecar lists the segment's keys to let merges reload and rebuild it.
sealed class Segment : IDisposable {
	public string Path { get; }
	public int Level { get; }
	readonly USearchIndex _index;

	public Segment(string path, int level) {
		Path = path;
		Level = level;
		_index = new USearchIndex(path, view: true);
	}

	// Builds an immutable segment file (+ key sidecar) from the given vectors and opens it.
	public static Segment Build(string directory, int dimensions,
		ReadOnlyMemory<ulong> ids, ReadOnlyMemory<ReadOnlyMemory<float>> vecs, int level) {
		var keys = ids.ToArray();
		var vectors = new float[keys.Length][];
		for (var i = 0; i < keys.Length; i++)
			vectors[i] = AsArray(vecs.Span[i]); // zero-copy: embedding vectors are full array-backed

		var path = System.IO.Path.Combine(directory, $"{Guid.NewGuid():N}.usearch");
		using (var index = new USearchIndex(MetricKind.Cos, ScalarKind.Int8, (ulong)dimensions)) {
			index.Add(keys, vectors);
			index.Save(path);
		}
		WriteKeys(KeysPath(path), keys);

		return new Segment(path, level);
	}

	// Merges a group into one segment at the given level by absorbing the smaller segments into the
	// largest (loaded mutable) — the biggest set is never re-inserted. Vectors move as their stored int8 codes —
	// no float dequantize/requantize round-trip, so the buffer is 4x smaller and bit-exact.
	public static Segment Merge(string directory, IReadOnlyList<Segment> group, int level, CancellationToken ct) {
		var ordered = group.OrderByDescending(s => s.Size).ToList();
		var largest = ordered[0];

		var path = System.IO.Path.Combine(directory, $"{Guid.NewGuid():N}.usearch");
		var keysTmp = KeysPath(path) + ".tmp";

		try {
			using (var merged = new USearchIndex(largest.Path, view: false)) // load the largest, mutable
			using (var keysOut = OpenWrite(keysTmp)) {
				// The merged sidecar starts with the largest segment's keys, then appends the absorbed ones.
				using (var baseKeys = OpenRead(KeysPath(largest.Path)))
					baseKeys.CopyTo(keysOut);

				foreach (var source in ordered.Skip(1)) {
					var keys = new List<ulong>();
					var vectorsInt8 = new List<sbyte[]>();
					var scanned = 0;
					foreach (var key in source.LoadKeys()) {
						if ((++scanned & 0xFFFF) == 0)
							ct.ThrowIfCancellationRequested();
						if (merged.Contains(key)) // already in the base or an earlier-absorbed source
							continue;
						if (source.TryGetInt8(key, out var vectorInt8)) {
							keys.Add(key);
							vectorsInt8.Add(vectorInt8);
						}
					}
					if (keys.Count == 0)
						continue;
					ct.ThrowIfCancellationRequested();
					merged.Add(keys.ToArray(), vectorsInt8.ToArray()); // batch: one reservation
					WriteKeys(keysOut, CollectionsMarshal.AsSpan(keys));
				}

				ct.ThrowIfCancellationRequested(); // bail before the expensive (uncancellable) Save
				merged.Save(path);
			}
		} catch (OperationCanceledException) {
			File.Delete(keysTmp); // discard the partial sidecar (Save never ran, so no .usearch exists)
			throw;
		}

		File.Move(keysTmp, KeysPath(path), overwrite: true);
		return new Segment(path, level);
	}

	// USearch.NET 2.x only accepts float[]; return the underlying array when the memory wraps a
	// full one, otherwise copy.
	static float[] AsArray(ReadOnlyMemory<float> vector) {
		if (MemoryMarshal.TryGetArray(vector, out ArraySegment<float> seg)
			&& seg.Offset == 0 && seg.Array is { } arr && seg.Count == arr.Length) {
			return arr;
		}
		return vector.ToArray();
	}

	public ulong Size => _index.Size();
	public bool Contains(ulong id) => _index.Contains(id);

	public int Search(ReadOnlyMemory<float> query, int limit, out ReadOnlyMemory<ulong> keys, out ReadOnlyMemory<float> distances) {
		var result = _index.Search(AsArray(query), limit, out var keysArr, out var distancesArr);
		keys = keysArr;
		distances = distancesArr;
		return result;
	}

	public bool TryGet(ulong id, out ReadOnlyMemory<float> vector) {
		if (_index.Get(id, out float[] arr) > 0) {
			vector = arr;
			return true;
		}
		vector = ReadOnlyMemory<float>.Empty;
		return false;
	}

	// The raw stored int8 vector (no dequantize) — used to move vectors between segments on merge.
	bool TryGetInt8(ulong id, out sbyte[] vectorInt8) {
		if (_index.Get(id, out sbyte[] arr) > 0) {
			vectorInt8 = arr;
			return true;
		}
		vectorInt8 = [];
		return false;
	}

	IEnumerable<ulong> LoadKeys() {
		using var stream = OpenRead(KeysPath(Path));

		var remaining = stream.Length;
		if (remaining % sizeof(ulong) != 0)
			throw new InvalidDataException($"Key sidecar '{KeysPath(Path)}' is not a whole number of keys.");

		var buffer = new byte[1 << 16]; // 64 KiB — a whole number of 8-byte keys
		while (remaining > 0) {
			var chunk = (int)Math.Min(buffer.Length, remaining); // a multiple of 8, like both operands
			stream.ReadExactly(buffer, 0, chunk);
			for (var offset = 0; offset < chunk; offset += sizeof(ulong))
				yield return BinaryPrimitives.ReadUInt64LittleEndian(buffer.AsSpan(offset));
			remaining -= chunk;
		}
	}

	static void WriteKeys(string keysPath, ReadOnlySpan<ulong> keys) {
		var tmp = keysPath + ".tmp";
		using (var stream = OpenWrite(tmp))
			WriteKeys(stream, keys);
		File.Move(tmp, keysPath, overwrite: true);
	}

	// Appends keys to an open stream in fixed-size chunks, little-endian.
	static void WriteKeys(Stream stream, ReadOnlySpan<ulong> keys) {
		var buffer = new byte[1 << 16]; // 64 KiB — a whole number of 8-byte keys
		var perChunk = buffer.Length / sizeof(ulong);

		for (var written = 0; written < keys.Length; written += perChunk) {
			var batch = Math.Min(perChunk, keys.Length - written);
			for (var i = 0; i < batch; i++)
				BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(i * sizeof(ulong)), keys[written + i]);
			stream.Write(buffer, 0, batch * sizeof(ulong));
		}
	}

	static FileStream OpenRead(string path) => new(path, FileMode.Open, FileAccess.Read,
		FileShare.Read, bufferSize: 1 << 16, FileOptions.SequentialScan);

	static FileStream OpenWrite(string path) => new(path, FileMode.Create, FileAccess.Write,
		FileShare.None, bufferSize: 1 << 16, FileOptions.SequentialScan);

	public void Dispose() => _index.Dispose();

	public void DisposeAndDelete() {
		_index.Dispose();
		File.Delete(Path);
		File.Delete(KeysPath(Path));
	}

	static string KeysPath(string segmentPath) => segmentPath + ".keys";
}
