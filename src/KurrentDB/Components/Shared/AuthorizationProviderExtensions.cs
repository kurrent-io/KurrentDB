// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Plugins.Authorization;

namespace KurrentDB.Components.Shared;

// Authorization helpers for the Blazor UI. The UI publishes core messages directly, bypassing the
// gRPC/HTTP front-ends where IAuthorizationProvider.CheckAccessAsync is normally invoked — so every
// privileged UI operation must perform the equivalent check itself, with the same Operation (and
// resource parameters) the matching front-end uses.
//
// EnsureAccessAsync is the throw-on-deny convenience used by the UI services; for UI affordances
// ("should this button be enabled / link shown?") call IAuthorizationProvider.CheckAccessAsync directly.
public static class AuthorizationProviderExtensions {
	// ValueTask mirrors IAuthorizationProvider.CheckAccessAsync and avoids a Task allocation on the
	// common synchronous-completion path (e.g. passthrough / cached policy decisions). Await it once.
	public static async ValueTask EnsureAccessAsync(
		this IAuthorizationProvider authorizationProvider, ClaimsPrincipal principal, Operation operation, CancellationToken ct) {
		if (!await authorizationProvider.CheckAccessAsync(principal, operation, ct))
			throw new AccessDeniedException();
	}
}

public sealed class AccessDeniedException() : Exception("Access denied.");
