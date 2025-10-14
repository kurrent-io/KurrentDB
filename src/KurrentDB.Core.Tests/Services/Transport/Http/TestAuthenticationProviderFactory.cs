// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.Security.Claims;
using EventStore.Plugins.Authentication;

namespace KurrentDB.Core.Tests.Services.Transport.Http;

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
