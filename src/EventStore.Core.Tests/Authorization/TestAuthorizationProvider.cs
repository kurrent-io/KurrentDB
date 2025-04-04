// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Plugins;
using EventStore.Plugins.Authorization;

namespace EventStore.Core.Tests.Authorization;

class TestAuthorizationProvider : Plugin, IAuthorizationProvider {
	public ValueTask<bool> CheckAccessAsync(ClaimsPrincipal principal, Operation operation, CancellationToken cancellationToken) =>
		ValueTask.FromResult(true);
}
