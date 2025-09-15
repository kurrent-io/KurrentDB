// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;

namespace KurrentDB.Core.RateLimiting;

// Represents a PartitionedRateLimiter that wraps PartitionedRateLimiters
// (unlike DefaultPartitionedRateLimiter which wraps ordinary RateLimiters)
// It therefore supports two levels of partitioning.
// TResource combines both levels and determines the overall type of the PartitionedRateLimiter.
// TResource1 is the first level.
// TResource2 is the second level.
public class TwoLevelPartitionedRateLimiter<TResource, TResource1, TResource2>
	: PartitionedRateLimiter<TResource>
	where TResource : ITupleResource<TResource1, TResource2>
	where TResource1 : struct, Enum {

	private readonly PartitionedRateLimiter<TResource2>[] _innerLimiters;

	public TwoLevelPartitionedRateLimiter(
		Func<int, PartitionedRateLimiter<TResource2>> firstLevelPartitionFactory) {

		// populate the _innerLimiters array, constructing one for each element in the Enum.
		var values = Enum.GetValues<TResource1>();
		var maxValue = values.MaxBy(x => Convert.ToInt32(x));
		_innerLimiters = new PartitionedRateLimiter<TResource2>[Convert.ToInt32(maxValue) + 1];
		foreach (var value in values) {
			var i = Convert.ToInt32(value);
			_innerLimiters[i] ??= firstLevelPartitionFactory(i);
		}
	}

	public override RateLimiterStatistics GetStatistics(TResource resourcePair) =>
		_innerLimiters[Convert.ToInt32(resourcePair.Item1)].GetStatistics(resourcePair.Item2);

	protected override ValueTask<RateLimitLease> AcquireAsyncCore(
		TResource resourcePair,
		int permitCount,
		CancellationToken cancellationToken) =>

		_innerLimiters[Convert.ToInt32(resourcePair.Item1)].AcquireAsync(resourcePair.Item2, permitCount, cancellationToken);

	protected override RateLimitLease AttemptAcquireCore(TResource resourcePair, int permitCount) =>
		_innerLimiters[Convert.ToInt32(resourcePair.Item1)].AttemptAcquire(resourcePair.Item2, permitCount);
}
