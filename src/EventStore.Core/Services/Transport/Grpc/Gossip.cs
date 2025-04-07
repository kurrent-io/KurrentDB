// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using EventStore.Core.Bus;
using EventStore.Core.Metrics;
using EventStore.Plugins.Authorization;

namespace EventStore.Core.Services.Transport.Grpc;

partial class Gossip : EventStore.Client.Gossip.Gossip.GossipBase {
	private readonly IPublisher _bus;
	private readonly IAuthorizationProvider _authorizationProvider;
	private readonly IDurationTracker _tracker;

	public Gossip(IPublisher bus, IAuthorizationProvider authorizationProvider, IDurationTracker tracker) {
		_bus = bus;
		_authorizationProvider =
			authorizationProvider ?? throw new ArgumentNullException(nameof(authorizationProvider));
		_tracker = tracker;
	}
}
