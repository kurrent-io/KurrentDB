// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Security.Claims;
using EventStore.Plugins.Authentication;
using Microsoft.AspNetCore.Http;

namespace EventStore.Auth.UserCertificates.Tests;

// grants anonymous claims to anyone
internal class FakeAnonymousHttpAuthenticationProvider : IHttpAuthenticationProvider {
	public static readonly ClaimsPrincipal Anonymous = new(new ClaimsIdentity(new Claim[] { new(ClaimTypes.Anonymous, ""), }));
	public string Name => "anonymous";

	public bool Authenticate(HttpContext context, out HttpAuthenticationRequest request) {
		request = new HttpAuthenticationRequest(context, "", "");
		request.Authenticated(Anonymous);
		return true;
	}
}
