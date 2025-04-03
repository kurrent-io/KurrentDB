// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Encodings.Web;
using System.Net.Http.Headers;
using System.Text;
using System;

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
