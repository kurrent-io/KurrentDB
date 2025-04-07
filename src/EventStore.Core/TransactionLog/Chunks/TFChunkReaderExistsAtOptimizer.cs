// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using EventStore.Core.DataStructures;
using ILogger = Serilog.ILogger;

namespace EventStore.Core.TransactionLog.Chunks;

public class TFChunkReaderExistsAtOptimizer {
	private static TFChunkReaderExistsAtOptimizer _instance;

	public static TFChunkReaderExistsAtOptimizer Instance {
		get {
			// we don't want two of these things so lock to make sure...
			if (_instance == null)
				lock (typeof(TFChunkReaderExistsAtOptimizer))
					if (_instance == null)
						_instance = new TFChunkReaderExistsAtOptimizer(MaxBloomFiltersCached);

			return _instance;
		}
	}

	//Least-Recently-Used cache to keep track of scavenged TFChunks that have cached bloom filters
	private readonly ILRUCache<string, TFChunk.TFChunk> _cache;

	private const int
		MaxBloomFiltersCached =
			10000; //around 5GB RAM max if we consider 200,000 log positions/chunk and 20 bits/log position

	private static readonly ILogger Log = Serilog.Log.ForContext<TFChunkReaderExistsAtOptimizer>();


	public TFChunkReaderExistsAtOptimizer(int maxCached) {
		Func<object, bool> onPut = (o) => {
			var chunk = (TFChunk.TFChunk)o;
			if (chunk == null)
				return false;
			Log.Debug("Optimizing chunk {chunk} for fast merge...", chunk.FileName);
			chunk.OptimizeExistsAt();
			return true;
		};

		Func<object, bool> onRemove = (o) => {
			var chunk = (TFChunk.TFChunk)o;
			if (chunk == null)
				return false;
			Log.Debug("Clearing fast merge optimizations from chunk {chunk}...", chunk.FileName);
			chunk.DeOptimizeExistsAt();
			return true;
		};

		_cache = new LRUCache<string, TFChunk.TFChunk>("ExistsAtOptimizedChunks", maxCached, onPut, onRemove);
	}

	public void Optimize(TFChunk.TFChunk chunk) {
		if (!chunk.ChunkHeader.IsScavenged)
			return;
		_cache.Put(chunk.FileName, chunk);
	}

	public bool IsOptimized(TFChunk.TFChunk chunk) {
		TFChunk.TFChunk value;
		return _cache.TryGet(chunk.FileName, out value);
	}

	public void DeOptimizeAll() {
		_cache.Clear();
	}
}
