// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Security.Claims;
using EventStore.Plugins.Authentication;
using KurrentDB.Core.Authentication.InternalAuthentication;
using KurrentDB.Core.Helpers;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Tests.Helpers;
using IODispatcherDelayedMessage = KurrentDB.Core.Helpers.IODispatcherDelayedMessage;

namespace KurrentDB.Core.Tests.Authentication;

public abstract class with_internal_authentication_provider<TLogFormat, TStreamId> : TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
	protected new IODispatcher _ioDispatcher;
	protected InternalAuthenticationProvider _internalAuthenticationProvider;

	protected void SetUpProvider() {
		_ioDispatcher = new IODispatcher(_bus, _bus);
		_bus.Subscribe<ClientMessage.ReadStreamEventsBackwardCompleted>(_ioDispatcher.BackwardReader);
		_bus.Subscribe<ClientMessage.NotHandled>(_ioDispatcher.BackwardReader);
		_bus.Subscribe(_ioDispatcher.ForwardReader);
		_bus.Subscribe(_ioDispatcher.Writer);
		_bus.Subscribe(_ioDispatcher.StreamDeleter);
		_bus.Subscribe(_ioDispatcher.Awaker);
		_bus.Subscribe<IODispatcherDelayedMessage>(_ioDispatcher);
		_bus.Subscribe<ClientMessage.NotHandled>(_ioDispatcher);

		_internalAuthenticationProvider = new(_bus, _ioDispatcher, new StubPasswordHashAlgorithm(), 1000, false, DefaultData.DefaultUserOptions);
		_bus.Subscribe(_internalAuthenticationProvider);
	}
}

class TestAuthenticationRequest(
	string name,
	string suppliedPassword,
	Action unauthorized,
	Action<ClaimsPrincipal> authenticated,
	Action error,
	Action notReady
) : AuthenticationRequest("test", new Dictionary<string, string> {
	["uid"] = name,
	["pwd"] = suppliedPassword
}) {
	public override void Unauthorized() => unauthorized();

	public override void Authenticated(ClaimsPrincipal principal) => authenticated(principal);

	public override void Error() => error();

	public override void NotReady() => notReady();
}
