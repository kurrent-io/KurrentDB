// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Core.Services.Monitoring.Stats;

public class GcStats {
	/// <summary>
	/// Number of Gen 0 Collections.
	/// </summary>
	public readonly long Gen0ItemsCount;
	/// <summary>
	/// Number of Gen 1 Collections.
	/// </summary>
	public readonly long Gen1ItemsCount;
	/// <summary>
	/// Number of Gen 2 Collections.
	/// </summary>
	public readonly long Gen2ItemsCount;
	/// <summary>
	/// Gen 0 heap size.
	/// </summary>
	public readonly long Gen0Size;
	/// <summary>
	/// Gen 1 heap size.
	/// </summary>
	public readonly long Gen1Size;
	/// <summary>
	/// Gen 2 heap size.
	/// </summary>
	public readonly long Gen2Size;
	/// <summary>
	/// Large Object Heap size.
	/// </summary>
	public readonly long LargeHeapSize;
	/// <summary>
	/// Memory allocation speed.
	/// </summary>
	public readonly float AllocationSpeed;
	/// <summary>
	/// </summary>
	public readonly float Fragmentation;
	/// <summary>
	/// % of Time in GC.
	/// </summary>
	public readonly float TimeInGc;
	/// <summary>
	/// Total Bytes in all Heaps.
	/// </summary>
	public readonly long TotalBytesInHeaps;

	public GcStats(long gcGen0Items,
		long gcGen1Items,
		long gcGen2Items,
		long gcGen0Size,
		long gcGen1Size,
		long gcGen2Size,
		long gcLargeHeapSize,
		float gcAllocationSpeed,
		float gcFragmentation,
		float gcTimeInGc,
		long gcTotalBytesInHeaps) {
		Gen0ItemsCount = gcGen0Items;
		Gen1ItemsCount = gcGen1Items;
		Gen2ItemsCount = gcGen2Items;
		Gen0Size = gcGen0Size;
		Gen1Size = gcGen1Size;
		Gen2Size = gcGen2Size;
		LargeHeapSize = gcLargeHeapSize;
		AllocationSpeed = gcAllocationSpeed;
		Fragmentation = gcFragmentation;
		TimeInGc = gcTimeInGc;
		TotalBytesInHeaps = gcTotalBytesInHeaps;
	}
}
