// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Plugins.Authorization;

namespace KurrentDB.Components.Tests.TestUtilities;

// Test double for IAuthorizationProvider that records every Operation it is asked about and returns a
// fixed verdict (deny by default). Lets a test assert BOTH that a UI service called the provider (and
// respected the verdict) AND that it passed the expected Operation (+ parameters) — i.e. no-bypass and
// fidelity in one. Use grant:false to verify denial short-circuits before any publish; grant:true to
// verify a permitted op proceeds.
public sealed class RecordingAuthorizationProvider(bool grant = false) : AuthorizationProviderBase {
	readonly List<Operation> _recorded = [];

	public IReadOnlyList<Operation> RecordedOperations => _recorded;
	public Operation? LastOperation => _recorded.Count > 0 ? _recorded[^1] : null;

	public override ValueTask<bool> CheckAccessAsync(ClaimsPrincipal principal, Operation operation, CancellationToken cancellationToken) {
		_recorded.Add(operation);
		return ValueTask.FromResult(grant);
	}
}
