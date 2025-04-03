// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Authentication.DelegatedAuthentication;
using EventStore.Core.Authentication.PassthroughAuthentication;
using EventStore.Plugins.Authentication;
using Microsoft.AspNetCore.Http;

namespace EventStore.Core.Services.Transport.Http.Authentication;

public class PassthroughHttpAuthenticationProvider(IAuthenticationProvider internalAuthenticationProvider) : IHttpAuthenticationProvider {
	private readonly IAuthenticationProvider _authenticationProvider = GetProvider(internalAuthenticationProvider);

	public string Name => "insecure";

	private static PassthroughAuthenticationProvider GetProvider(IAuthenticationProvider provider) =>
		provider switch {
			PassthroughAuthenticationProvider p => p,
			DelegatedAuthenticationProvider d => GetProvider(d.Inner),
			_ => throw new ArgumentException("PassthroughHttpAuthenticationProvider can be initialized only with a PassthroughAuthenticationProvider.")
		};

	public bool Authenticate(HttpContext context, out HttpAuthenticationRequest request) {
		request = new(context, null!, null!);
		_authenticationProvider.Authenticate(request);
		return true;
	}
}
