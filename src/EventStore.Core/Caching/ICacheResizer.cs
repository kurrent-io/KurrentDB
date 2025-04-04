// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;

namespace EventStore.Core.Caching;

// The resizer is responsible for resizing its associated cache.
// The resizer can be part of a hierarchy of resizers.
// Children can _reserve_ capacity, making it unavailable to be distributed by weight.
//     Capacity = ReservedCapacity + UnreservedCapacity
// To trigger resizing, the parent calls the CalcCapacity method, passing in
//   - its unreserved capacity (i.e. the capacity to be shared among the children by weight)
//   - total weight of the children (so that they can draw the correct amount according to their weight)
public interface ICacheResizer {
	string Name { get; }

	ResizerUnit Unit { get; }

	// How much of the capacity given to this resizer is reserved.
	// The remaining (unreserved) capacity can be allotted by weight among the children.
	long ReservedCapacity { get; }

	// Weight relative to siblings when drawing from the unreserved capacity
	int Weight { get; }

	// How much of the capacity is currently utilized
	long Size { get; }

	// Number of items in cache
	long Count { get; }

	// Approximate amount freed but not yet garbage collected
	long FreedSize { get; }

	void CalcCapacity(long unreservedCapacity, int totalWeight);

	void ResetFreedSize();

	IEnumerable<CacheStats> GetStats(string parentKey);
}

// We support Entries for backwards compatibility. In the future all cache
// sizes will likely be specified in bytes and we can remove this enum.
public enum ResizerUnit {
	Bytes,
	Entries,
}

public static class CacheResizerExtensions {
	// Helper for the top level to just pass in the totalCapacity
	public static void CalcCapacityTopLevel(this ICacheResizer self, long totalCapacity) {
		self.CalcCapacity(
			unreservedCapacity: totalCapacity - self.ReservedCapacity,
			totalWeight: self.Weight);
	}
}
