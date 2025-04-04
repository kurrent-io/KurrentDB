// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.Bus;
using EventStore.Plugins.Authorization;

namespace EventStore.Core.Services.Transport.Grpc;

internal partial class Redaction : Kurrent.Client.Redaction.Redaction.RedactionBase {
	private readonly IPublisher _bus;
	private readonly IAuthorizationProvider _authorizationProvider;

	public Redaction(IPublisher bus, IAuthorizationProvider authorizationProvider) {
		_bus = bus;
		_authorizationProvider = authorizationProvider;
	}
}
