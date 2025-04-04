// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json;
using EventStore.POC.IO.Core;

namespace EventStore.AutoScavenge.Tests;

public class DummyClient : IClient {
	private long _count;
	public bool BeNaughty { get; set; }

	public Task WriteMetaDataMaxCountAsync(string stream, CancellationToken cancellationToken) {
		return Task.CompletedTask;
	}

	public Task<long> WriteAsync(string stream, EventToWrite[] events, long expectedVersion, CancellationToken cancellationToken) {
		if (BeNaughty)
			throw new Exception("CrowdStrike !");

		return Task.FromResult(_count++);
	}

	public IAsyncEnumerable<Event> SubscribeToAll(FromAll start, CancellationToken cancellationToken) {
		return AsyncEnumerable.Empty<Event>();
	}

	public IAsyncEnumerable<Event> SubscribeToStream(string stream, CancellationToken cancellationToken) {
		if (stream != "$mem-gossip")
			return AsyncEnumerable.Empty<Event>();

		var id = Guid.NewGuid();
		var gossip = new GossipMessage {
			NodeId = id,
			Members = [ new ClusterMember {
					InstanceId = id,
					State = "Leader",
					IsAlive = true,
					InternalHttpEndPointIp = "foobar",
					InternalHttpEndPointPort = 42,
					WriterCheckpoint = 0,
					IsReadOnlyReplica = false
				}
			]
		};

		return AsyncEnumerable.ToAsyncEnumerable([
			new Event(Guid.NewGuid(), DateTime.Now, "$mem-gossip", 0, "$gossip", "application/json", 0, 0, false,
				JsonSerializer.SerializeToUtf8Bytes(gossip), Array.Empty<byte>())
		]);
	}

	public IAsyncEnumerable<Event> ReadStreamForwards(string stream, long maxCount, CancellationToken cancellationToken) {
		return AsyncEnumerable.Empty<Event>();
	}

	public IAsyncEnumerable<Event> ReadStreamBackwards(string stream, long maxCount, CancellationToken cancellationToken) {
		return AsyncEnumerable.Empty<Event>();
	}

	public IAsyncEnumerable<Event> ReadAllBackwardsAsync(Position position, long maxCount, CancellationToken cancellationToken) {
		throw new NotImplementedException();
	}

	public Task DeleteStreamAsync(string stream, long expectedVersion, CancellationToken cancellationToken) {
		throw new NotImplementedException();
	}
}
