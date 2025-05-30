// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Plugins.Authentication;
using KurrentDB.Core.Services.UserManagement;
using Microsoft.AspNetCore.Http;

namespace KurrentDB.Core.Services.Transport.Http.Authentication;

public class UnixSocketAuthenticationProvider : IHttpAuthenticationProvider {
	public string Name => "unix-socket";

	public bool Authenticate(HttpContext context, out HttpAuthenticationRequest request) {
		if (context.IsUnixSocketConnection()) {
			request = new(context, "system", "");
			request.Authenticated(SystemAccounts.System);
			return true;
		}

		request = null!;
		return false;
	}
}
