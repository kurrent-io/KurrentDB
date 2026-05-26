// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Buffers.Binary;
using KurrentDB.Core.DataStructures.ProbabilisticFilter;
using Microsoft.Extensions.Logging;

namespace KurrentDB.Kontext.Workspaces.Runtime;

public sealed class WorkspaceBloomFilters : IDisposable {
	const string PositionsFileName = "bloom.positions";
	const string StreamHashesFileName = "bloom.streamhashes";

	const long PositionsFilterSizeBytes = 50L * 1024 * 1024;
	const long StreamHashesFilterSizeBytes = 5L * 1024 * 1024;

	readonly PersistentBloomFilter _positions;
	readonly PersistentBloomFilter _streamHashes;
	readonly ILogger<WorkspaceBloomFilters> _logger;

	public WorkspaceBloomFilters(string workspaceDir, ILogger<WorkspaceBloomFilters> logger) {
		_logger = logger;
		Directory.CreateDirectory(workspaceDir);
		_positions = Open(Path.Combine(workspaceDir, PositionsFileName), PositionsFilterSizeBytes);
		_streamHashes = Open(Path.Combine(workspaceDir, StreamHashesFileName), StreamHashesFilterSizeBytes);
	}

	static PersistentBloomFilter Open(string path, long sizeBytes) =>
		new(File.Exists(path)
			? FileStreamPersistence.FromFile(path)
			: new FileStreamPersistence(sizeBytes, path, create: true));

	public bool MightContainLogPosition(ulong position) {
		Span<byte> key = stackalloc byte[8];
		BinaryPrimitives.WriteUInt64LittleEndian(key, position);
		return _positions.MightContain(key);
	}

	public bool MightContainStreamHash(ulong streamHash) {
		Span<byte> key = stackalloc byte[8];
		BinaryPrimitives.WriteUInt64LittleEndian(key, streamHash);
		return _streamHashes.MightContain(key);
	}

	public void AddLogPosition(ulong position) {
		Span<byte> key = stackalloc byte[8];
		BinaryPrimitives.WriteUInt64LittleEndian(key, position);
		_positions.Add(key);
	}

	public void AddStreamHash(ulong streamHash) {
		Span<byte> key = stackalloc byte[8];
		BinaryPrimitives.WriteUInt64LittleEndian(key, streamHash);
		_streamHashes.Add(key);
	}

	Task _flush = Task.CompletedTask;

	public void Flush(bool throttle) {
		if (throttle)
			ThrottledFlush();
		else
			UnthrottledFlush();
	}

	void ThrottledFlush() {
		if (!_flush.IsCompleted)
			return;

		_flush = Task.Run(() => {
			try {
				_positions.Flush(throttle: true);
				_streamHashes.Flush(throttle: true);
			} catch (Exception ex) {
				// best effort: the filters are an optimization only, and the
				// unthrottled flush at dispose persists everything anyway
				_logger.LogWarning(ex, "Background bloom filter flush failed");
			}
		});
	}

	void UnthrottledFlush() {
		// a background flush may still be in flight, and flushes are not re-entrant
		_flush.Wait();
		_positions.Flush(throttle: false);
		_streamHashes.Flush(throttle: false);
	}

	public void Dispose() {
		Flush(throttle: false);
		_positions.Dispose();
		_streamHashes.Dispose();
	}
}
