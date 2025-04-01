// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Security.Claims;
using EventStore.Plugins.Authentication;
using Microsoft.AspNetCore.Routing;

namespace EventStore.Auth.UserCertificates.Tests;

// pretends any user that the valid certificate names is in the database
internal class FakeAuthenticationProvider(string scheme) : AuthenticationProviderBase(name: "fake") {
	public override void Authenticate(AuthenticationRequest authenticationRequest) {
		if (!authenticationRequest.HasValidClientCertificate) {
			authenticationRequest.Unauthorized();
			return;
		}

		authenticationRequest.Authenticated(new ClaimsPrincipal(new ClaimsIdentity(
			new[] {
				new Claim(ClaimTypes.Name, authenticationRequest.Name),
			},
			"fake")));
	}

	public override IReadOnlyList<string> GetSupportedAuthenticationSchemes() => [scheme];
}
