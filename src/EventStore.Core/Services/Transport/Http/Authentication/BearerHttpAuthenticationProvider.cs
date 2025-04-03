// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net.Http.Headers;
using EventStore.Plugins.Authentication;
using Microsoft.AspNetCore.Http;

namespace EventStore.Core.Services.Transport.Http.Authentication;

public class BearerHttpAuthenticationProvider(IAuthenticationProvider internalAuthenticationProvider) : IHttpAuthenticationProvider {
	public string Name => "bearer";

	public bool Authenticate(HttpContext context, out HttpAuthenticationRequest request) {
		if (!context.Request.Headers.TryGetValue("authorization", out var values) || values.Count != 1 ||
		    !AuthenticationHeaderValue.TryParse(values[0], out var authenticationHeader) || authenticationHeader.Scheme != "Bearer") {
			request = null!;
			return false;
		}

		request = new(context, authenticationHeader.Parameter!);
		internalAuthenticationProvider.Authenticate(request);
		return true;
	}
}
