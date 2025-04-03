// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net.Http.Headers;
using System.Text;
using EventStore.Plugins.Authentication;
using Microsoft.AspNetCore.Http;

namespace EventStore.Core.Services.Transport.Http.Authentication;

public class BasicHttpAuthenticationProvider(IAuthenticationProvider internalAuthenticationProvider) : IHttpAuthenticationProvider {
	public string Name => "basic";

	public bool Authenticate(HttpContext context, out HttpAuthenticationRequest request) {
		if (context.Request.Headers.TryGetValue("authorization", out var values) && values.Count == 1 &&
		    AuthenticationHeaderValue.TryParse(values[0], out var authenticationHeader) && authenticationHeader.Scheme == "Basic" &&
		    TryDecodeCredential(authenticationHeader.Parameter, out var username, out var password)) {
			request = new HttpAuthenticationRequest(context, username, password);
			internalAuthenticationProvider.Authenticate(request);
			return true;
		}

		request = null!;
		return false;
	}

	private static bool TryDecodeCredential(string value, out string username, out string password) {
		username = password = default;

		var stringValue = Encoding.UTF8.GetString(System.Convert.FromBase64String(value));
		var index = stringValue.IndexOf(':');
		if (index < 0) {
			return false;
		}

		username = stringValue[..index];
		password = stringValue[(index + 1)..];

		return true;
	}
}
