// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System.Collections.Generic;
using System.Security.Claims;
using EventStore.Plugins.Authentication;

namespace EventStore.Core.Tests.Common.ClusterNodeOptionsTests;

public class TestAuthenticationProviderFactory : IAuthenticationProviderFactory {
	public IAuthenticationProvider Build(bool logFailedAuthenticationAttempts) =>
		new TestAuthenticationProvider();
}

public class TestAuthenticationProvider() : AuthenticationProviderBase(name: "test") {
	public override void Authenticate(AuthenticationRequest authenticationRequest) =>
		authenticationRequest.Authenticated(
			new(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, authenticationRequest.Name) }))
		);

	public override IReadOnlyList<string> GetSupportedAuthenticationSchemes() => ["Basic"];
}
