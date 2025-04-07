// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

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
