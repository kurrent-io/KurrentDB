// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using EventStore.Common.Utils;

namespace EventStore.Core.Caching;

public class CompositeCacheResizer : CacheResizer, ICacheResizer {
	private readonly ICacheResizer[] _children;

	public CompositeCacheResizer(string name, int weight, params ICacheResizer[] children)
		: base(GetUniqueUnit(children), new CompositeCache(name, children)) {
		Weight = weight;
		_children = children;
		ReservedCapacity = _children.Sum(x => x.ReservedCapacity);
	}

	public int Weight { get; }

	public long ReservedCapacity { get; }

	public void CalcCapacity(long unreservedCapacity, int totalWeight) {
		var totalCapacity = unreservedCapacity + ReservedCapacity;
		Cache.SetCapacity(totalCapacity.ScaleByWeight(Weight, totalWeight));
	}

	public void ResetFreedSize() {
		Cache.ResetFreedSize();
	}

	public IEnumerable<CacheStats> GetStats(string parentKey) {
		var key = BuildStatsKey(parentKey);
		var size = 0L;
		var count = 0L;
		var numChildren = 0;

		foreach (var child in _children) {
			foreach (var childStats in child.GetStats(key)) {
				if (GetParentKey(childStats.Key) == key) {
					size += childStats.Size;
					count += childStats.Count;
					numChildren++;
				}

				yield return childStats;
			}
		}

		yield return new CacheStats(key, Name, Cache.Capacity, size, count, numChildren);
	}

	private static ResizerUnit GetUniqueUnit(ICacheResizer[] children) {
		if (children.Length < 1)
			throw new ArgumentException("There must be at least one child", nameof(children));

		var unit = children[0].Unit;

		if (children.Any(x => x.Unit != unit))
			throw new ArgumentException("All children must have the same unit", nameof(children));

		return unit;
	}

	class CompositeCache : IDynamicCache {
		private readonly ICacheResizer[] _children;
		private readonly int _childrenWeight;
		private readonly long _reservedCapacity;

		public CompositeCache(string name, params ICacheResizer[] children) {
			Ensure.NotNullOrEmpty(name, nameof(name));
			Name = name;
			_children = children;
			_childrenWeight = children.Sum(static x => x.Weight);
			_reservedCapacity = children.Sum(static x => x.ReservedCapacity);
		}

		public string Name { get; }

		public long Capacity { get; private set; }

		public void SetCapacity(long value) {
			// todo: could consider taking into account capacity not used by children because
			// they took their maximum values
			Capacity = value;
			var unreservedCapacity = Math.Max(Capacity - _reservedCapacity, 0);
			foreach (var child in _children) {
				child.CalcCapacity(unreservedCapacity, _childrenWeight);
			}
		}

		public void ResetFreedSize() {
			foreach (var child in _children)
				child.ResetFreedSize();
		}

		public long Size => _children.Sum(static x => x.Size);
		public long Count => _children.Sum(static x => x.Count);
		public long FreedSize => _children.Sum(static x => x.FreedSize);
	}
}
