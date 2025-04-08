// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Plugins.Authorization;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Metrics;

namespace EventStore.Core.Services.Transport.Grpc;

partial class Gossip : EventStore.Client.Gossip.Gossip.GossipBase {
	private readonly IPublisher _bus;
	private readonly IAuthorizationProvider _authorizationProvider;
	private readonly IDurationTracker _tracker;

	public Gossip(IPublisher bus, IAuthorizationProvider authorizationProvider, IDurationTracker tracker) {
		_bus = bus;
		_authorizationProvider = Ensure.NotNull(authorizationProvider);
		_tracker = tracker;
	}
}
