// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;

namespace KurrentDB.Core.RateLimiting;

//
// Overall explanation
//
// https://devblogs.microsoft.com/dotnet/announcing-rate-limiting-for-dotnet/
//
// We want to limit the number of requests accessing the memory/filesystem/archive/... at once.
// We want to provide a unified rate limiter for the server to use, thereby abstracting way the details.
// Therefore consider a PartitionedRateLimiter<> that wraps ConcurrentLimiters.
// BUT, as well as partitioning by memory/filesystem/archive, we (probably) want to partition by priority.
// It is NOT that we want to partition by both dimensions independently (i.e. x low priority requests and y high priority requests)
// No, we want x concurrent requests across ALL priorities.
// We therefore use a PartitionedRateLimiter<> that wraps PartitionedRateLimiter<>s
//   where the inner PartitionRateLimiter<>s are PriorityConcurrencyLimiters.




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

// Partitions by priority for the queueing, but shares the pool of leases between all the partitions.
// it is important that we respect FIFO order of requests with the same priority
public class PriorityConcurrencyLimiter : PartitionedRateLimiter<PriorityEx> {
	private readonly IBoundedAsyncPriorityQueue<AcquisitionRequest> _queue;
	private int _poolSize;
	private int _permitsInUse;

	//qq we are quite granular about acquiring this lock, perhaps we should acquire it more coarsely
	// perhaps we should consider passing it in to the queue so it can use the same synchronization object
	// or the priority queue maybe needn't be thread safe
	private object Lock => _queue;

	public PriorityConcurrencyLimiter(int capacityPerPriority, int concurrencyLimit) {
		_poolSize = concurrencyLimit;
		_queue = new BoundedAsyncPriorityQueue<AcquisitionRequest>(
			capacityPerPriority: capacityPerPriority,
			x => x.Priority);
	}

	public override RateLimiterStatistics GetStatistics(PriorityEx priority) =>
		throw new NotImplementedException();

	private void Release(int permitCount) {
		lock (Lock) {
			_permitsInUse -= permitCount;
			// we have just freed up some permits, maybe the next request can be served
			PumpQueue();
		}
	}

	protected override async ValueTask<RateLimitLease> AcquireAsyncCore(
		PriorityEx priority,
		int permitsRequested,
		CancellationToken cancellationToken) {

		if (TryLease(permitsRequested)) {
			// the permits were already available
			return Lease.CreateAcquired(this, permitsRequested);
		} else {
			// the permits were not available, perhaps we can queue for them
			//qq if the acquisitionrequest does include allocated a tcs, avoid doing this if the queue is full.
			var acquisitionRequest = new AcquisitionRequest(permitsRequested, priority);
			if (_queue.TryEnqueue(acquisitionRequest)) {
				//qq we have just put a request in the queue, it can be immediately serviceable
				// e.g. if we are a high priority request for 1 permit, and the previous head of the queue
				// was a medium priority request for 3 tokens with only 2 available.
				//qq it might be better to detect that synchronously.
				PumpQueue();
				await acquisitionRequest.Task;
				return Lease.CreateAcquired(this, permitsRequested);
			} else {
				// the queue is full
				return Lease.NotAcquired;
			}
		}
	}

	protected override RateLimitLease AttemptAcquireCore(PriorityEx priority, int permitsRequested) {
		return TryLease(permitsRequested)
			? Lease.CreateAcquired(this, permitsRequested)
			: Lease.NotAcquired;
	}

	private void PumpQueue() {
		lock (Lock) {
			if (_queue.TryPeek(out var acquisitionRequest)) {
				if (TryLeaseNoQueueCheck(acquisitionRequest.PermitCount)) {
					_queue.TryRead(out _);
					acquisitionRequest.Complete();
				}
			} else {
				// no requests in queue, nothing to do
			}
		}
	}

	private bool TryLease(int permitsRequested) {
		lock (Lock) {
			if (_queue.Count is 0) {
				return TryLeaseNoQueueCheck(permitsRequested);
			} else {
				// queue is not empty, there might be permits available but not enough
				// for the head of the queue. we can't take them.
			}
			return false;
		}
	}

	private bool TryLeaseNoQueueCheck(int permitsRequested) {
		lock (Lock) {
			var permitsWouldBeInUse = _permitsInUse + permitsRequested;
			if (permitsWouldBeInUse <= _poolSize) {
				_permitsInUse = permitsWouldBeInUse;
				return true;
			}
			return false;
		}
	}

	//qq can we do it more efficiently than this? IValueTaskSource?
	readonly record struct AcquisitionRequest(int PermitCount, PriorityEx Priority) {
		private readonly TaskCompletionSource _tcs = new();
		public Task Task => _tcs.Task;
		public void Complete() => _tcs.TrySetResult();
	}

	class Lease : RateLimitLease {
		public static Lease NotAcquired = new(false, null, 0);

		private readonly PriorityConcurrencyLimiter _limiter;
		private readonly int _permitCount;
		private bool _disposed;

		public static Lease CreateAcquired(PriorityConcurrencyLimiter limiter, int permitCount) =>
			new(true, limiter, permitCount);

		private Lease(bool isAcquired, PriorityConcurrencyLimiter limiter, int permitCount) {
			IsAcquired = isAcquired;
			_limiter = limiter;
			_permitCount = permitCount;
		}

		public override bool IsAcquired { get; }

		public override IEnumerable<string> MetadataNames => [];

		public override bool TryGetMetadata(string metadataName, out object metadata) {
			metadata = default;
			return false;
		}

		protected override void Dispose(bool disposing) {
			if (_disposed)
				return;

			_disposed = true;

			_limiter?.Release(_permitCount);
		}
	}
}
