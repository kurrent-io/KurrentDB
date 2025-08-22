// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using DotNext.Runtime.CompilerServices;
using KurrentDB.Core.RateLimiting;
using Xunit;

namespace KurrentDB.Core.XUnit.Tests.RateLimiting;

// want to see which seems like a more natural fit from the callers perspective (StorageReaderService)
// desirable properties:
// - minimal processing on the main queue
//     - processing the rejection if we can do it synchronously is fine
// - minimal unnecessary allocations
public class RateLimitingTests {
	public static IEnumerable<SingleSourceRequest> GenerateRequests() {
		for (var i = 0; i < 1000; i++) {
			yield return new SingleSourceRequest($"{i}", Source.Archive, new(Priority.High, Continued: false));
		}
	}

	// this allocates a lease for every request?
	[Fact]
	public void Test() {
		var limiter = new TwoLevelPartitionedRateLimiter<Resource, Source, PriorityEx>(
			_ => new PriorityConcurrencyLimiter(
				capacityPerPriority: 50,
				//qq concurrencyLimit wants to be different per source and dynamically changeable
				concurrencyLimit: 100));

		// main thread pumping the request queue (StorageReaderService)
		foreach (var request in GenerateRequests()) {
			ProcessRequest(request, CancellationToken.None);
		}

		void ProcessRequest(SingleSourceRequest r, CancellationToken token) {
			var task = limiter.AcquireAsync(
				new(r.Source, r.Priority),
				permitCount: 1,
				token);
			if (task.IsCompletedSuccessfully) {
				// got a lease synchronously
				var lease = task.Result;
				if (lease.IsAcquired) {
					// got the permits, process the request asynchronously
					_ = ProcessWithLease(r, lease, token);
				} else {
					// did not get the permits
					r.Reject("lease was synchronously rejected");
				}
			} else if (task.IsCompleted) {
				// completed synchronously but not successfully (error, canceled?)
				r.Reject("lease was synchronously unsuccessful");
			} else {
				// not completed synchronously
				_ = AcquireAndProcess(r, task, token);
			}
		}

		//qq all of the processing occurs off of the main thread
		// but every request allocates a task
		[AsyncMethodBuilder(typeof(SpawningAsyncTaskMethodBuilder))]
		async Task AcquireAndProcess(SingleSourceRequest request, ValueTask<RateLimitLease> acquisitionTask, CancellationToken token) {
			var lease = await acquisitionTask;
			await ProcessWithLease(request, lease, token);
		}

		async Task ProcessWithLease(SingleSourceRequest request, RateLimitLease lease, CancellationToken token) {
			using var _ = lease;
			if (lease.IsAcquired) {
				await request.Process(token);
			} else {
				request.Reject("lease was asynchronously rejected");
			}
		}
	}
}
