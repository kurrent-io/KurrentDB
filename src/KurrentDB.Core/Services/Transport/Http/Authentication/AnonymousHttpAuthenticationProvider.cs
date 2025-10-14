// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Plugins.Authentication;
using KurrentDB.Core.Services.UserManagement;
using Microsoft.AspNetCore.Http;

namespace KurrentDB.Core.Services.Transport.Http.Authentication;

public class AnonymousHttpAuthenticationProvider : IHttpAuthenticationProvider {
	public string Name => "anonymous";

	public bool Authenticate(HttpContext context, out HttpAuthenticationRequest request) {
		request = new(context, null!, null!);
		request.Authenticated(SystemAccounts.Anonymous);
		return true;
	}
}
