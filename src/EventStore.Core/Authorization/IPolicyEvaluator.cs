// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Plugins.Authorization;

namespace EventStore.Core.Authorization;

public interface IPolicyEvaluator {
	ValueTask<EvaluationResult> EvaluateAsync(ClaimsPrincipal cp, Operation operation,
		CancellationToken cancellationToken);
}
