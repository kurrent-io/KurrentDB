// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading.RateLimiting;

namespace KurrentDB.Core.RateLimiting;

static class RateLimiters {
	public static PartitionedRateLimiter<ResourceAndPriority> General =
		new TwoLevelPartitionedRateLimiter<ResourceAndPriority, Resource, PriorityEx>(
			_ => new PriorityConcurrencyLimiter(
				capacityPerPriority: 50,
				concurrencyLimit: 100));
}
