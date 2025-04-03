// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.Threading.Tasks;
using EventStore.Core.Services.Transport.Http;
using EventStore.Core.Services.Transport.Http.Authentication;
using EventStore.Core.Services.UserManagement;
using EventStore.Plugins.Authentication;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Http;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.Transport.Http.Authentication;

public class TestFixtureWithUnixSocketAuthenticationProvider {
	protected UnixSocketAuthenticationProvider _provider;

	protected void SetUpProvider() {
		_provider = new UnixSocketAuthenticationProvider();
	}
}

class ConnectionItemsFeature : IConnectionItemsFeature {
	public IDictionary<object, object> Items { get; set; }

	public ConnectionItemsFeature() {
		Items = new Dictionary<object, object>();
	}
}

[TestFixture]
public class when_handling_a_request_not_having_the_unix_socket_connection_item : TestFixtureWithUnixSocketAuthenticationProvider {
	private bool _authenticateResult;

	[SetUp]
	public void SetUp() {
		SetUpProvider();
		var context = new DefaultHttpContext();
		_authenticateResult = _provider.Authenticate(context, out _);
	}

	[Test]
	public void returns_false() {
		Assert.IsFalse(_authenticateResult);
	}
}

[TestFixture]
public class when_handling_a_request_having_the_unix_socket_connection_item : TestFixtureWithUnixSocketAuthenticationProvider {
	private bool _authenticateResult;
	private HttpAuthenticationRequest _authenticateRequest;

	[SetUp]
	public void SetUp() {
		SetUpProvider();
		var context = new DefaultHttpContext();
		var connectionItemsFeature = new ConnectionItemsFeature();
		connectionItemsFeature.Items.Add(UnixSocketConnectionMiddleware.UnixSocketConnectionKey, true);
		context.Features.Set<IConnectionItemsFeature>(connectionItemsFeature);

		_authenticateResult = _provider.Authenticate(context, out _authenticateRequest);
	}

	[Test]
	public void returns_true() {
		Assert.IsTrue(_authenticateResult);
	}

	[Test]
	public async Task passes_authentication() {
		var (status, _) = await _authenticateRequest.AuthenticateAsync();
		Assert.AreEqual(HttpAuthenticationRequestStatus.Authenticated, status);
	}

	[Test]
	public async Task sets_user_to_system_user() {
		var (_, user) = await _authenticateRequest.AuthenticateAsync();
		Assert.AreEqual(SystemAccounts.System.Claims, user.Claims);
	}
}
