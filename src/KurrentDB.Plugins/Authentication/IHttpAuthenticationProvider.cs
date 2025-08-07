// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Microsoft.AspNetCore.Http;

namespace EventStore.Plugins.Authentication;

public interface IHttpAuthenticationProvider {
	/// <summary>
	///     Return a unique name used to externally identify the authentication provider.
	/// </summary>
	string Name { get; }

	bool Authenticate(HttpContext context, out HttpAuthenticationRequest request);
}
