// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using EventStore.Common.Utils;
using EventStore.Core.Bus;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Metrics;
using EventStore.Core.Services.TimerService;
using EventStore.Core.Util;
using Serilog;

namespace EventStore.Core.Caching;

public class DynamicCacheManager:
	IHandle<MonitoringMessage.DynamicCacheManagerTick>,
	IHandle<MonitoringMessage.InternalStatsRequest> {

	protected static readonly ILogger Log = Serilog.Log.ForContext<DynamicCacheManager>();

	private readonly IPublisher _bus;
	private readonly Func<long> _getFreeSystemMem;
	private readonly Func<long> _getFreeHeapMem;
	private readonly Func<int> _getGcCollectionCount;
	private readonly long _totalMem;
	private readonly int _keepFreeMemPercent;
	private readonly long _keepFreeMemBytes;
	private readonly long _keepFreeMem;
	private readonly TimeSpan _minResizeInterval;
	private readonly long _minResizeThreshold;
	private readonly ICacheResizer _rootCacheResizer;
	private readonly Message _scheduleTick;
	private readonly Func<Dictionary<string, CacheStats>> _fetchOrGetCachedStats;
	private readonly ICacheResourcesTracker _cacheResourcesTracker;

	private DateTime _lastResize = DateTime.UtcNow;
	private long _lastAvailableMem;
	private long _lastGcCount;

	public DynamicCacheManager(
		IPublisher bus,
		Func<long> getFreeSystemMem,
		Func<long> getFreeHeapMem,
		Func<int> getGcCollectionCount,
		long totalMem,
		int keepFreeMemPercent,
		long keepFreeMemBytes,
		TimeSpan monitoringInterval,
		TimeSpan minResizeInterval,
		long minResizeThreshold,
		ICacheResizer rootCacheResizer,
		ICacheResourcesTracker cacheResourcesTracker) {

		if (keepFreeMemPercent is < 0 or > 100)
			throw new ArgumentException($"{nameof(keepFreeMemPercent)} must be between 0 to 100 inclusive.");

		if (keepFreeMemBytes < 0)
			throw new ArgumentException($"{nameof(keepFreeMemBytes)} must be non-negative.");

		_bus = bus;
		_getFreeSystemMem = getFreeSystemMem;
		_getFreeHeapMem = getFreeHeapMem;
		_getGcCollectionCount = getGcCollectionCount;
		_totalMem = totalMem;
		_keepFreeMemPercent = keepFreeMemPercent;
		_keepFreeMemBytes = keepFreeMemBytes;
		_keepFreeMem = Math.Max(_keepFreeMemBytes, _totalMem.ScaleByPercent(_keepFreeMemPercent));
		_minResizeInterval = minResizeInterval;
		_minResizeThreshold = minResizeThreshold;
		_rootCacheResizer = rootCacheResizer;
		_scheduleTick = TimerMessage.Schedule.Create(
			monitoringInterval,
			_bus,
			new MonitoringMessage.DynamicCacheManagerTick());
		_cacheResourcesTracker = cacheResourcesTracker;
		_fetchOrGetCachedStats = Functions.Debounce(
			() => _rootCacheResizer.GetStats(string.Empty).ToDictionary(stats => stats.Key),
			TimeSpan.FromSeconds(1));
	}

	private readonly struct AvailableMemoryInfo {
		public long AvailableMem { get; } // available memory for static and dynamic caches
		public long FreeSystemMem { get; } // free memory on the system
		public long FreeHeapMem { get; } // free memory within the heap
		public long CachedMem { get; } // approximate amount of memory used by static and dynamic caches
		public long FreedMem { get; } // approximate amount of managed memory freed by caches but not yet collected
		public long TotalFreeMem => FreeSystemMem + FreeHeapMem + FreedMem;

		public AvailableMemoryInfo(long availableMem, long freeSystemMem, long freeHeapMem, long cachedMem, long freedMem) {
			AvailableMem = availableMem;
			FreeSystemMem = freeSystemMem;
			FreeHeapMem = freeHeapMem;
			CachedMem = cachedMem;
			FreedMem = freedMem;
		}
	}

	public void Start() {
		if (_rootCacheResizer.Unit == ResizerUnit.Entries) {
			_rootCacheResizer.CalcCapacityTopLevel(_rootCacheResizer.ReservedCapacity);
		} else {
			var availableMem = GetAvailableMemoryInfo().AvailableMem;
			_lastAvailableMem = availableMem;
			ResizeCaches(availableMem);

			Thread.MemoryBarrier();

			Tick();
		}

		var allStats = _fetchOrGetCachedStats();
		foreach (var stat in allStats.Values) {
			var key = stat.Key;
			Log.Information("Cache {key} capacity initialized to {capacity:N0} {unit}",
				key, stat.Capacity, _rootCacheResizer.Unit);
			
			if (stat.NumChildren == 0)
				_cacheResourcesTracker.Register(stat.Name, _rootCacheResizer.Unit, () => _fetchOrGetCachedStats()[key]);
		}
	}

	public void Handle(MonitoringMessage.DynamicCacheManagerTick message) {
		ThreadPool.QueueUserWorkItem(_ => {
			try {
				Thread.MemoryBarrier();
				ResizeCachesIfNeeded();
				Thread.MemoryBarrier();
			} finally {
				Tick();
			}
		});
	}

	public void Handle(MonitoringMessage.InternalStatsRequest message) {
		Thread.MemoryBarrier(); // just to ensure we're seeing latest values

		var stats = new Dictionary<string, object>();
		var cachesStats = _rootCacheResizer.GetStats(string.Empty);

		foreach(var cacheStat in cachesStats) {
			var statNamePrefix = $"es-{cacheStat.Key}-";
			stats[statNamePrefix + "name"] = cacheStat.Name;
			stats[statNamePrefix + "count"] = cacheStat.Count;
			stats[statNamePrefix + "size" + _rootCacheResizer.Unit] = cacheStat.Size;
			stats[statNamePrefix + "capacity" + _rootCacheResizer.Unit] = cacheStat.Capacity;
			stats[statNamePrefix + "utilizationPercent"] = cacheStat.UtilizationPercent;
		}

		message.Envelope.ReplyWith(new MonitoringMessage.InternalStatsRequestResponse(stats));
	}

	private bool ResizeConditionsMet(out AvailableMemoryInfo availableMemInfo) {
		availableMemInfo = GetAvailableMemoryInfo();

		// not much has changed since the last resize, don't resize again.
		if (Math.Abs(availableMemInfo.AvailableMem - _lastAvailableMem) < _minResizeThreshold)
			return false;

		// we're low on memory, resize.
		if (availableMemInfo.TotalFreeMem < _keepFreeMem)
			return true;

		// we've not resized for a while now, resize.
		if (DateTime.UtcNow - _lastResize >= _minResizeInterval)
			return true;

		return false;
	}

	private void ResizeCachesIfNeeded() {
		if (FullGcHasRun())
			_rootCacheResizer.ResetFreedSize();

		if (!ResizeConditionsMet(out var availableMemInfo))
			return;

		Log.Debug("Resizing caches.");

		if (availableMemInfo.TotalFreeMem < _keepFreeMem)
			Log.Verbose("Total free memory is lower than "
			          + "{thresholdPercent}% or {thresholdBytes:N0} bytes.\n"
			          + "Free system memory: {freeSystemMem:N0} bytes.\n"
			          + "Free heap memory: {freeHeapMem:N0} bytes.\n"
			          + "Freed memory: {freedMem:N0} bytes.\n\n"
			          + "Total free memory: {totalFreeMem:N0} bytes.\n",
				_keepFreeMemPercent,
				_keepFreeMemBytes,
				availableMemInfo.FreeSystemMem,
				availableMemInfo.FreeHeapMem,
				availableMemInfo.FreedMem,
				availableMemInfo.TotalFreeMem);

		try {
			ResizeCaches(availableMemInfo.AvailableMem);
		} catch (Exception ex) {
			Log.Error(ex, "Error while resizing caches");
		} finally {
			_lastResize = DateTime.UtcNow;
			_lastAvailableMem = availableMemInfo.AvailableMem;
		}
	}

	private bool FullGcHasRun() =>
		_lastGcCount < (_lastGcCount = _getGcCollectionCount());

	private void ResizeCaches(long availableMem) {
		_rootCacheResizer.CalcCapacityTopLevel(availableMem);
	}

	private AvailableMemoryInfo GetAvailableMemoryInfo() {
		do {
			var freeSystemMem = _getFreeSystemMem();
			var freeHeapMem = _getFreeHeapMem();
			var cachedMem = _rootCacheResizer.Size;
			var freedMem = _rootCacheResizer.FreedSize;

			var availableMem = Math.Max(0L, freeSystemMem + freeHeapMem + cachedMem + freedMem - _keepFreeMem);

			Log.Verbose("Calculating memory available for caching based on:\n" +
			            "Total memory: {totalMem:N0} bytes\n" +
			            "Free system memory: {freeSystemMem:N0} bytes\n" +
			            "Free heap memory: {freeHeapMem:N0} bytes\n" +
			            "Cached memory: ~{cachedMem:N0} bytes\n" +
			            "Freed memory: ~{freedMem:N0} bytes\n" +
			            "Keep free memory: {keepFreeMem:N0} bytes (higher of {keepFreeMemPercent}% total mem & {keepFreeMemBytes:N0} bytes)\n\n" +
			            "Memory available for caching: ~{availableMem:N0} bytes\n",
				_totalMem, freeSystemMem, freeHeapMem, cachedMem, freedMem,
				_keepFreeMem, _keepFreeMemPercent, _keepFreeMemBytes,
				availableMem);

			if (FullGcHasRun())
				_rootCacheResizer.ResetFreedSize();
			else
				return new AvailableMemoryInfo(availableMem, freeSystemMem, freeHeapMem, cachedMem, freedMem);
		} while (true);
	}

	private void Tick() => _bus.Publish(_scheduleTick);
}
