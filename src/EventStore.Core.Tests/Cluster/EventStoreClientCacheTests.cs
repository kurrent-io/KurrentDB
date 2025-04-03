// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using EventStore.Common.Utils;
using EventStore.Core.Bus;
using EventStore.Core.Cluster;
using EventStore.Core.Messages;
using EventStore.Core.Metrics;
using EventStore.Core.Services.TimerService;
using EventStore.Core.Services.Transport.Http.NodeHttpClientFactory;
using EventStore.Core.Tests.Fakes;
using NUnit.Framework;

namespace EventStore.Core.Tests.Cluster;

public class EventStoreClientCacheTests {
	private static readonly INodeHttpClientFactory NodeHttpClientFactory = new NodeHttpClientFactory(
		uriScheme: Uri.UriSchemeHttps,
		nodeCertificateValidator: delegate { return (true, null); },
		clientCertificateSelector: null);

	private static readonly Func<EndPoint, IPublisher, EventStoreClusterClient> EventStoreClusterClientFactory =
		(endpoint, bus) =>
			new EventStoreClusterClient(
				bus, Uri.UriSchemeHttps, endpoint, NodeHttpClientFactory, null,
				new DurationTracker.NoOp(),
				new DurationTracker.NoOp());

	[Test]
	public void BusShouldNotBeNull() {
		Assert.Throws<ArgumentNullException>(() =>
			new EventStoreClusterClientCache(null, EventStoreClusterClientFactory));
	}

	[Test]
	public void ClientFactoryShouldNotBeNull() {
		Assert.Throws<ArgumentNullException>(() =>
			new EventStoreClusterClientCache(new FakePublisher(), null));
	}

	[Test]
	public void CanGetClientForEndpoint() {
		var sut = new EventStoreClusterClientCache(new FakePublisher(), EventStoreClusterClientFactory);

		var client = sut.Get(new IPEndPoint(IPAddress.Loopback, 1113));

		Assert.AreEqual(client, sut.Get(new IPEndPoint(IPAddress.Loopback, 1113)));
	}

	[Test]
	public async Task CleansCacheOnThreshold() {
		var interval = TimeSpan.FromMinutes(30);
		var oldItemThreshold = TimeSpan.FromMilliseconds(500);
		var sut = new EventStoreClusterClientCache(new FakePublisher(), EventStoreClusterClientFactory, interval,
			oldItemThreshold);
		var oldClient = sut.Get(new IPEndPoint(IPAddress.Loopback, 1113));

		sut.Handle(new ClusterClientMessage.CleanCache());
		await Task.Delay(oldItemThreshold.Add(TimeSpan.FromMilliseconds(500)));

		var newClient = sut.Get(new IPEndPoint(IPAddress.Loopback, 1113));
		newClient = sut.Get(new IPEndPoint(IPAddress.Loopback, 1113));
		Assert.AreNotEqual(oldClient, newClient);
	}

	[Test]
	public void ShouldScheduleCacheCleanOnTimer() {
		var interval = TimeSpan.FromMilliseconds(1);
		var bus = new FakePublisher();
		var sut = new EventStoreClusterClientCache(bus, EventStoreClusterClientFactory, interval, interval);

		sut.Handle(new SystemMessage.SystemInit());

		Assert.True(bus.Messages.OfType<TimerMessage.Schedule>().Count() == 1);
	}

	[Test]
	public async Task ShouldDisposeClientOnceEvictedFromCache() {
		var interval = TimeSpan.FromMinutes(30);
		var oldItemThreshold = TimeSpan.FromMilliseconds(500);
		var sut = new EventStoreClusterClientCache(new FakePublisher(), EventStoreClusterClientFactory, interval,
			oldItemThreshold);
		var client = sut.Get(new IPEndPoint(IPAddress.Loopback, 1113));
		Assert.NotNull(client);

		await Task.Delay(oldItemThreshold.Add(TimeSpan.FromMilliseconds(500)));
		sut.Handle(new ClusterClientMessage.CleanCache());
		// Give the cache enough time to dispose the client
		await Task.Delay(oldItemThreshold);

		Assert.True(client.Disposed);
	}
}
