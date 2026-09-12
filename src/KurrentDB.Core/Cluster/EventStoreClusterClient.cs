// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Cluster;
using Grpc.Net.Client;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Metrics;
using KurrentDB.Core.Services.Transport.Http.NodeHttpClientFactory;
using EndPoint = System.Net.EndPoint;

// ReSharper disable once CheckNamespace
namespace EventStore.Core.Cluster;


public partial class EventStoreClusterClient : IDisposable {
	private readonly Gossip.GossipClient _gossipClient;
	private readonly Elections.ElectionsClient _electionsClient;

	private readonly GrpcChannel _channel;
	private readonly IPublisher _bus;
	private readonly string _clusterDns;
	private readonly IDurationTracker _gossipSendTracker;
	private readonly IDurationTracker _gossipGetTracker;

	public bool Disposed { get; private set; }

	public EventStoreClusterClient(
		IPublisher bus,
		string uriScheme,
		EndPoint nodeEndPoint,
		INodeHttpClientFactory nodeHttpClientFactory,
		string clusterDns,
		IDurationTracker gossipSendTracker,
		IDurationTracker gossipGetTracker) {

		_clusterDns = clusterDns;

		_channel = nodeHttpClientFactory.CreateChannel(uriScheme, nodeEndPoint);
		var callInvoker = _channel.CreateCallInvoker();
		_gossipClient = new Gossip.GossipClient(callInvoker);
		_electionsClient = new Elections.ElectionsClient(callInvoker);
		_bus = bus;
		_gossipSendTracker = gossipSendTracker;
		_gossipGetTracker = gossipGetTracker;
	}

	public void Dispose() {
		if (Disposed)
			return;
		_channel.Dispose();
		Disposed = true;
	}
}
