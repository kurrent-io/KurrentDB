// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Security.Claims;
using System.Threading.Tasks;
using EventStore.Plugins.Authorization;

namespace EventStore.Auth.LegacyAuthorizationWithStreamAuthorizationDisabled;

public class LegacyStreamPermissionAssertion : IAssertion {
	public AssertionInformation Information { get; } =
		new AssertionInformation("stream", "legacy acl", Grant.Unknown);

	public Grant Grant { get; } = Grant.Unknown;

	public ValueTask<bool> Evaluate(ClaimsPrincipal cp, Operation operation, PolicyInformation policy,
		EvaluationContext context) {
		var streamId = FindStreamId(operation.Parameters.Span);

		return CheckStreamAccess(operation, policy, context, streamId);
	}

	private ValueTask<bool> CheckStreamAccess(Operation operation, PolicyInformation policy,
		EvaluationContext context, string streamId) {
		if (streamId == null) {
			context.Add(new AssertionMatch(policy,
				new AssertionInformation("streamId", "streamId is null", Grant.Deny)));
			return new ValueTask<bool>(true);
		}

		if ((streamId == string.Empty || streamId == "$all") &&
		    (operation == Operations.Streams.Delete || operation == Operations.Streams.Write)) {
			context.Add(new AssertionMatch(policy,
				new AssertionInformation("streamId", $"{operation.Action} denied on $all", Grant.Deny)));
			return new ValueTask<bool>(true);
		}

		context.Add(new AssertionMatch(policy,
			new AssertionInformation("stream", "public stream", Grant.Allow)));

		return new ValueTask<bool>(true);
	}

	private string FindStreamId(ReadOnlySpan<Parameter> parameters) {
		for (int i = 0; i < parameters.Length; i++) {
			if (parameters[i].Name == "streamId")
				return parameters[i].Value;
		}

		throw new InvalidOperationException();
	}
}
