// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EventStore.AutoScavenge.Tests;

public class DummyAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions> {
	private static readonly Claim[] AdminClaims = new[] {
		new Claim(ClaimTypes.Name, "ouro"),
		new Claim(ClaimTypes.Role, "$admins")
	};

	private static readonly Claim[] UserClaims = new[] {
		new Claim(ClaimTypes.Name, "johndoe"),
	};

	public DummyAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
		UrlEncoder encoder) : base(options, logger, encoder) {
	}

	protected override Task<AuthenticateResult> HandleAuthenticateAsync() {
		try {
			if (!Request.Headers.TryGetValue("Authorization", out var authHeader))
				return Task.FromResult(AuthenticateResult.Fail("Missing Authorization Header"));

			var authHeaderVal = AuthenticationHeaderValue.Parse(authHeader!);

			if (authHeaderVal.Scheme.Equals("dummy", StringComparison.OrdinalIgnoreCase)) {
				var credentials = Encoding.UTF8
					.GetString(Convert.FromBase64String(authHeaderVal.Parameter!))
					.Split(':');

				if (credentials.Length == 2) {
					var username = credentials[0];
					var password = credentials[1];

					if (username == "ouro" && password == "changeit") {
						var identity = new ClaimsIdentity(AdminClaims, Scheme.Name);
						var principal = new ClaimsPrincipal(identity);
						var ticket = new AuthenticationTicket(principal, Scheme.Name);

						return Task.FromResult(AuthenticateResult.Success(ticket));
					}

					if (username == "johndoe" && password == "1234") {
						var identity = new ClaimsIdentity(UserClaims, Scheme.Name);
						var principal = new ClaimsPrincipal(identity);
						var ticket = new AuthenticationTicket(principal, Scheme.Name);

						return Task.FromResult(AuthenticateResult.Success(ticket));
					}
				}
			}
		} catch {
			return Task.FromResult(AuthenticateResult.Fail("invalid authorization header"));
		}

		return Task.FromResult(AuthenticateResult.Fail("invalid username or password"));
	}
}
