// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;

namespace EventStore.Security.EncryptionAtRest.Transforms.DataStructures;

// implements the SIEVE cache eviction algorithm: https://cachemon.github.io/SIEVE-website/

public class SieveBlockCache {
	private readonly int _cacheSize;
	private readonly int _blockDataSize;

	private readonly Dictionary<int, CacheItem> _cacheDict = new();
	private int _count;
	private CacheItem _first, _last, _hand;

	public SieveBlockCache(int cacheSize, int blockDataSize) {
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cacheSize);
		ArgumentOutOfRangeException.ThrowIfNegative(blockDataSize);

		_cacheSize = cacheSize;
		_blockDataSize = blockDataSize;
	}

	public void Put(int blockNumber, ReadOnlySpan<byte> blockData) {
		CacheItem item;
		if (_count >= _cacheSize) {
			var o = _hand ?? _last;

			while (o!.Visited) {
				o.Visited = false;
				o = o.Previous ?? _last;
			}

			_hand = o.Previous;
			Remove(o);
			item = o;
		} else {
			item = new CacheItem {
				BlockNumber = -1,
				BlockData = new byte[_blockDataSize],
				Visited = false
			};
		}

		item.BlockNumber = blockNumber;
		blockData.CopyTo(item.BlockData.Span);
		item.Visited = false;

		Add(item);
	}


	private void Add(CacheItem item) {
		_cacheDict.Add(item.BlockNumber, item);

		if (_first is not null)
			_first.Previous = item;

		item.Previous = null;
		item.Next = _first;
		_first = item;
		_last ??= item;

		_count++;
	}

	private void Remove(CacheItem item) {
		_cacheDict.Remove(item.BlockNumber);

		if (_first == _last) {
			_first = null;
			_last = null;
		} else if (item == _first) {
			_first = item.Next;
			_first.Previous = null;
		} else if (item == _last) {
			_last = item.Previous;
			_last.Next = null;
		} else {
			item.Previous.Next = item.Next;
			item.Next.Previous = item.Previous;
		}

		_count--;
	}

	public bool TryGet(int blockNumber, Span<byte> blockData) {
		if (!_cacheDict.TryGetValue(blockNumber, out var item))
			return false;

		item.BlockData.Span.CopyTo(blockData);
		item.Visited = true;
		return true;
	}

	private class CacheItem {
		public int BlockNumber { get; set; }
		public Memory<byte> BlockData { get; init; }
		public bool Visited { get; set; }

		public CacheItem Next { get; set; }
		public CacheItem Previous { get; set; }
	}
}
