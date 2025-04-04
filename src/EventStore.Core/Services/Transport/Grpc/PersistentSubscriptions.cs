// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using EventStore.Core.Bus;
using EventStore.Plugins.Authorization;

namespace EventStore.Core.Services.Transport.Grpc;

internal partial class PersistentSubscriptions
	: EventStore.Client.PersistentSubscriptions.PersistentSubscriptions.PersistentSubscriptionsBase {
	private readonly IPublisher _publisher;
	private readonly IAuthorizationProvider _authorizationProvider;

	public PersistentSubscriptions(IPublisher publisher, IAuthorizationProvider authorizationProvider) {
		if (publisher == null)
			throw new ArgumentNullException(nameof(publisher));
		if (authorizationProvider == null)
			throw new ArgumentNullException(nameof(authorizationProvider));
		_publisher = publisher;
		_authorizationProvider = authorizationProvider;
	}
}
