// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using DotNext.Runtime.Caching;
using Serilog;

namespace KurrentDB.Projections.Core.Services.Processing.V2;

public sealed class PartitionStateCache : IAsyncDisposable {
	private static readonly ILogger Log = Serilog.Log.ForContext<PartitionStateCache>();

	// RandomAccessCache requires TValue : notnull, so we wrap nullable strings in a record.
	private readonly record struct CachedValue(string? State);

	private readonly RandomAccessCache<string, CachedValue> _cache;
	private readonly string _name;
	private readonly string _projectionName;
	private long _evictions;
	private long _count;

	public PartitionStateCache(int capacity, string name, string projectionName) {
		if (capacity <= 0)
			throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be positive");
		_name = name;
		_projectionName = projectionName;
		_cache = new RandomAccessCache<string, CachedValue>(capacity) {
			Eviction = OnEviction,
			KeyComparer = StringComparer.Ordinal,
		};
	}

	public long Evictions => Interlocked.Read(ref _evictions);
	public long Count => Interlocked.Read(ref _count);

	public bool TryGet(string key, out string? value) {
		if (_cache.TryRead(key, out var session)) {
			using (session) {
				value = session.Value.State;
				return true;
			}
		}

		value = null;
		return false;
	}

	public async ValueTask Set(string key, string? value, CancellationToken ct) {
		// Sync Dispose, not DisposeAsync: DotNext recommends sync disposal on non-rate-limited
		// caches because DisposeAsync serializes writers behind a single promotion thread.
		using var session = await _cache.ChangeAsync(key, ct);
		var isNew = !session.TryGetValue(out _);
		session.SetValue(new CachedValue(value));
		if (isNew)
			Interlocked.Increment(ref _count);
	}

	public ValueTask DisposeAsync() => _cache.DisposeAsync();

	private void OnEviction(string key, CachedValue _) {
		Interlocked.Increment(ref _evictions);
		Interlocked.Decrement(ref _count);
		Log.Verbose("Partition state cache {Cache} for projection {Projection} evicted partition {Partition}",
			_name, _projectionName, key);
	}
}
