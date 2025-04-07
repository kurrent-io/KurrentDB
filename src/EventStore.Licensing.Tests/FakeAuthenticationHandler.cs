// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EventStore.Licensing.Tests;

public class FakeAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions> {
	public FakeAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
		ILoggerFactory logger, UrlEncoder encoder)
		: base(options, logger, encoder) {
	}

	public const string Username = "the_user";
	public const string Password = "the_password";

	protected override Task<AuthenticateResult> HandleAuthenticateAsync() {
		try {
			var authHeader = AuthenticationHeaderValue.Parse(Context.Request.Headers.Authorization!);
			var credentialBytes = Convert.FromBase64String(authHeader.Parameter ?? "");
			var credentials = Encoding.UTF8.GetString(credentialBytes).Split(':', 2);
			var username = credentials[0];
			var password = credentials[1];

			if (username == Username && password == Password) {
				var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] {
					new Claim(ClaimTypes.Name, Username),
					new Claim(ClaimTypes.Role, "$ops"),
				}, "fake"));
				var ticket = new AuthenticationTicket(principal, "TestScheme");

				return Task.FromResult(AuthenticateResult.Success(ticket));
			}
		} catch {
			// noop
		}

		return Task.FromResult(AuthenticateResult.Fail("no"));
	}
}
