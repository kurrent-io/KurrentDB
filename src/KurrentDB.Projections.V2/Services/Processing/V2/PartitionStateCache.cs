// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DotNext.Diagnostics.Metrics;
using DotNext.Runtime.Caching;
using DotNext.Runtime.InteropServices;
using DotNext.Threading;
using Serilog;
using Serilog.Events;

namespace KurrentDB.Projections.Core.Services.Processing.V2;

/// <summary>
/// Partition state cache that keeps keys and values in unmanaged memory, so the number of
/// cached partitions doesn't contribute to GC heap size or pause times.
/// </summary>
public sealed class PartitionStateCache : IAsyncDisposable {
	private static readonly ILogger Log = Serilog.Log.ForContext<PartitionStateCache>();

	private readonly RandomAccessCache<StateHandle> _cache;
	private readonly string _id = Guid.NewGuid().ToString();
	private readonly UpDownCounterObserver<long> _entries;
	private readonly MeterListener _listener;
	private readonly string _name;
	private readonly string _projectionName;
	private long _evictions;

	public PartitionStateCache(int capacity, string name, string projectionName) {
		if (capacity <= 0)
			throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be positive");
		_name = name;
		_projectionName = projectionName;
		_entries = new(_id.IsThisCache);
		_listener = new MeterListenerBuilder()
			.Observe(IsEntriesCount, _entries)
			.Build();
		_listener.Start();

		// Keys are compared as raw UTF-16 bytes (see RandomAccessCache.AsKey), which is ordinal comparison.
		_cache = new RandomAccessCache<StateHandle>(capacity) {
			Eviction = OnRetired,
			MeasurementTags = new() { { MeasurementHelpers.CacheIdTag, _id } },
		};

		static bool IsEntriesCount(Instrument instrument) =>
			instrument.Meter.Name is "DotNext.Runtime.Caching.RandomAccessCache.Unmanaged" && instrument.Name is "entries-count";
	}

	public long Evictions => Atomic.Read(ref _evictions);

	// Allocated entries, including evicted or replaced ones that are still waiting to be released
	public long Count => _entries.Value;

	public bool TryGet(string key, out string? value) {
		if (_cache.TryRead(RandomAccessCache.AsKey(key), out var session)) {
			using (session) {
				value = session.Value.Decode();
				return true;
			}
		}

		value = null;
		return false;
	}

	public async ValueTask Set(string key, string? value, CancellationToken ct) {
		var cacheKey = RandomAccessCache.AsKey(key);
		var state = StateHandle.Allocate(value);
		try {
			// State handlers re-serialize the state on every event, so an unchanged state arrives
			// as a new string with the same content. Skip the write to avoid remove + insert.
			// A partition key is written by a single partition processor, so the record can't
			// change between this check and the replacement below.
			if (_cache.TryRead(cacheKey, out var current)) {
				using (current) {
					if (current.Value.ContentEquals(state))
						return;
				}
			}

			// Records are immutable once inserted, so an overwrite removes the old record first.
			// It's flagged so that its retirement, which may happen later on the eviction thread,
			// isn't counted as an eviction.
			if (await _cache.TryRemoveAsync(cacheKey, ct) is { } removed) {
				using (removed) {
					removed.Value.MarkReplaced();
				}
			}

			// Sync Dispose, not DisposeAsync: DotNext recommends sync disposal on non-rate-limited
			// caches because DisposeAsync serializes writers behind a single promotion thread.
			using (var session = await _cache.ReplaceAsync(cacheKey, ct)) {
				session.SetValue(state);
			}

			// The cache owns the state now, it's released by OnRetired
			state = default;
		} finally {
			state.Dispose();
		}
	}

	// Releases the remaining records through OnRetired, which frees their unmanaged state.
	public async ValueTask DisposeAsync() {
		await _cache.DisposeAsync();
		_listener.Dispose();
	}

	// Called once per record when it's evicted or replaced and no sessions reference it anymore,
	// so freeing the state here can't race with a reader.
	private void OnRetired(ReadOnlySpan<byte> key, StateHandle state) {
		try {
			if (state.IsReplaced)
				return;

			Interlocked.Increment(ref _evictions);
			if (Log.IsEnabled(LogEventLevel.Verbose)) {
				Log.Verbose("Partition state cache {Cache} for projection {Projection} evicted partition {Partition}",
					_name, _projectionName, MemoryMarshal.Cast<byte, char>(key).ToString());
			}
		} finally {
			state.Dispose();
		}
	}

	// Header of the unmanaged block that holds the partition state: [Header][UTF-8 bytes].
	// The length prefix allows decoding without scanning for a terminator, and allows null state (Length < 0).
	private struct Header {
		public int Length;
		public bool Replaced;
	}

	// Owning handle to the unmanaged block. Freed exactly once, by OnRetired.
	private readonly struct StateHandle : IDisposable {
		private const int NullLength = -1;

		private readonly Pointer<Header> _header;

		private StateHandle(Pointer<Header> header) => _header = header;

		public static StateHandle Allocate(string? state) {
			var length = state is null ? 0 : Encoding.UTF8.GetByteCount(state);
			var header = new Pointer<Header>(Marshal.AllocHGlobal(Unsafe.SizeOf<Header>() + length));
			header.Value = new() { Length = state is null ? NullLength : length };
			var handle = new StateHandle(header);
			if (state is not null)
				Encoding.UTF8.GetBytes(state, handle.Payload);

			return handle;
		}

		private ref Header Header => ref _header.Value;

		private Span<byte> Payload => ((Pointer<byte>)(_header + 1)).AsSpan(int.Max(Header.Length, 0));

		public bool IsReplaced => Volatile.Read(in Header.Replaced);

		public void MarkReplaced() => Volatile.Write(ref Header.Replaced, true);

		public string? Decode() => Header.Length is NullLength
			? null
			: Encoding.UTF8.GetString(Payload);

		public bool ContentEquals(StateHandle other)
			=> Header.Length == other.Header.Length && Payload.SequenceEqual(other.Payload);

		// No-op for the default handle
		public void Dispose() => Marshal.FreeHGlobal(_header.Address);
	}
}

file static class MeasurementHelpers {
	// The meter is shared by all unmanaged caches, so the measurements of this cache are tagged with a unique id
	public const string CacheIdTag = "kurrentdb.partition-state-cache.id";

	public static bool IsThisCache(this string tag, UpDownCounter<long> instrument, ReadOnlySpan<KeyValuePair<string, object?>> tags) {
		foreach (var (key, value) in tags) {
			if (key is CacheIdTag)
				return ReferenceEquals(value, tag);
		}

		return false;
	}
}
