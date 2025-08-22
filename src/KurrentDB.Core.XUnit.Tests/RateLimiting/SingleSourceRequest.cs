// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using DotNext;
using KurrentDB.Core.Messaging;

namespace KurrentDB.Core.RateLimiting;

// A prioritised request that needs to access a particular source
public class SingleSourceRequest(string Name, Source source, PriorityEx priority) : Message {
	public Source Source => source;
	public PriorityEx Priority => priority;

	public async ValueTask Process(CancellationToken token) {
		Console.WriteLine($"Processing {Name}...");
		await Task.Delay(100, token);
		Console.WriteLine($"DONE Processing {Name}.");
	}

	public void Reject(string message) {
		Console.WriteLine($"REJECTED {Name}. {message}");
	}
}

//qqqqqqq maybe we need to worry a little bit about scheduling, what if the queue fills up and overflows just because
// the consumer hasn't had a chance to be scheduled? either the queues need to be long enough for that not to be a problem
// or maybe the continuation should run synchronously when we signal the asyncresetevent
// A higher level request that may need access to various sources during its processing

//qqqq are any of the reader messages queue affine? no i expect.
// if we did need affine processing, each seat would need its own dedicated queue.
public record MultiSourceRequest(int Name, PriorityEx Priority) {
	// The triage is quick and determines next steps
	public void Triage() {
		var isStreamRead = Random.Shared.NextBoolean();
		if (isStreamRead) {
			// stream read
			// we need to access the index first.
			// then we will know the chunks that we need to access.
			// is the index
			//   - part of the disk source,
			//   - a disk source but with a bumped priority?
			//   - its own source (how will the user size it though, perhaps we can calculate it based on the disk source)
			//   - depends on where the data will actually come from (we cant do this at the moment)
			//    at the moment it always comes from a filestream, we don't know if the OS
			//    has it in the page cache or not, it's more likely to be in the page cache
			//    than when we read a chunk.
			//qq avoid the case where a request has queued for the index and when queueing again the queue is full.
			// this also applies to any case where we are queueing a second time.
			// maybe it means that the continuation queues should be unbounded, but the initial queues should
			// take account of the size of the pair of queues.
			var indexRead = new SingleSourceRequest($"{Name}-Index", Source.FileSystem, Priority);

			//qq maybe this is analogous to asynchronous programming using callbacks vs using async/await
			// it might be analogous to imperative vs object oriented programming.
			//
			// maybe the question is how flexible the path of the request through the system needs to be.
			// message passing is nice if we have a simple flow?
			// request-centric is nice if different requests have different flows?
			//
			// in request centric the request can say 'right i need access to this resource, now ive
			// queried it i know what to do next' and it can accumulate that decision state in local variables.
			//
			// in message passing the processor needs to look at the request and see 'this one needs to talk to the index'
			// so it sends the message over to the index.
			//
			// there has to be logic _somewhere_ for marshalling the request through the various resources that it
			// needs to queue for. this plainly makes most sense in a request oriented perspective.
			//
			// which means if we wrote it in a message passing way, we would run into accidental complexity
			// perhaps we should try that far enough to witness the complexity
			//
			// 
		} else {
			// $all read
			// these know which chunk they are starting in and read linearly forwards or
			// backwards, not knowing which chunk they will finish in.
			// we cannot scatter-gather these
			// so we will make a SSR for the chunk that we need first
			//
			// if we cross to a chunk in another chunk we release the lease and get a new one
			// with a contiuation priority.
			//    if the new chunk is in the same source then we could optimise this by
			//    holding on to the lease, but i doubt this is important if it is tricky.


			//qqqqqqqq the grpc services break the ClientRequests up into MSRs
			// it should also bump to continuation priority for subsequent reads.
			// at least, if its timeout is not infinity.
			//qqq be careful not to exceed the ptable/chunk reader limits
			//qqq an lrucache metric showing the distribution of reads from the cache by age could be interesting
		}
	}
	public async ValueTask Process(CancellationToken token) {
		
		Console.WriteLine($"Processing {Name}...");
		await Task.Delay(100, token);
		Console.WriteLine($"DONE Processing {Name}.");
	}
}
