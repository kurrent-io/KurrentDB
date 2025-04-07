// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System.Net.Http.Headers;
using System.Text;
using EventStore.Plugins.Authentication;
using Microsoft.AspNetCore.Http;

namespace EventStore.Core.Services.Transport.Http.Authentication;

public class BasicHttpAuthenticationProvider : IHttpAuthenticationProvider {
	private readonly IAuthenticationProvider _internalAuthenticationProvider;

	public string Name => "basic";

	public BasicHttpAuthenticationProvider(IAuthenticationProvider internalAuthenticationProvider) {
		_internalAuthenticationProvider = internalAuthenticationProvider;
	}

	public bool Authenticate(HttpContext context, out HttpAuthenticationRequest request) {
		if (context.Request.Headers.TryGetValue("authorization", out var values) && values.Count == 1 &&
			AuthenticationHeaderValue.TryParse(values[0], out var authenticationHeader) && authenticationHeader.Scheme == "Basic" &&
			TryDecodeCredential(authenticationHeader.Parameter, out var username, out var password)) {
			request = new HttpAuthenticationRequest(context, username, password);
			_internalAuthenticationProvider.Authenticate(request);
			return true;
		}

		request = null;
		return false;
	}

	private static bool TryDecodeCredential(string value, out string username, out string password) {
		username = password = default;

		var parts = Encoding.UTF8.GetString(System.Convert.FromBase64String(value)).Split(':', 2);
		if (parts.Length < 2) {
			return false;
		}

		username = parts[0];
		password = parts[1];

		return true;
	}
}
