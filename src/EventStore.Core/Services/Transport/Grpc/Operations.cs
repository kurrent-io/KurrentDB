// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Common.Utils;
using EventStore.Core.Bus;
using EventStore.Plugins.Authorization;

namespace EventStore.Core.Services.Transport.Grpc;

internal partial class Operations : EventStore.Client.Operations.Operations.OperationsBase {
	private readonly IPublisher _publisher;
	private readonly IAuthorizationProvider _authorizationProvider;

	public Operations(IPublisher publisher, IAuthorizationProvider authorizationProvider) {
		_publisher = Ensure.NotNull(publisher);
		_authorizationProvider = Ensure.NotNull(authorizationProvider);
	}
}
