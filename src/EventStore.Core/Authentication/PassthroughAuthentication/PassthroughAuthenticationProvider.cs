// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable

using System.Collections.Generic;
using EventStore.Core.Services.UserManagement;
using EventStore.Plugins.Authentication;

namespace EventStore.Core.Authentication.PassthroughAuthentication;

public class PassthroughAuthenticationProvider() : AuthenticationProviderBase(name: "insecure", diagnosticsName: "PassthroughAuthentication") {
	public override void Authenticate(AuthenticationRequest authenticationRequest) =>
		authenticationRequest.Authenticated(SystemAccounts.System);

	public override IReadOnlyList<string> GetSupportedAuthenticationSchemes() => ["Insecure"];
}
