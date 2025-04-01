// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Security.Claims;
using EventStore.Core.Authorization;
using EventStore.Core.Services;
using EventStore.Plugins.Authorization;

namespace EventStore.Auth.StreamPolicyPlugin;

public class StreamPolicyAssertion(Func<string, AccessPolicy> getAccessPolicyForStream) : IStreamPermissionAssertion {
	public Grant Grant { get; } = Grant.Unknown;
	public AssertionInformation Information { get; } = new("starts with", "stream prefix", Grant.Unknown);

	public ValueTask<bool> Evaluate(ClaimsPrincipal cp, Operation operation, PolicyInformation policy,
		EvaluationContext context) {
		var streamId = FindStreamId(operation.Parameters.Span);

		return CheckStreamAccess(cp, operation, policy, context, streamId);
	}

	private async ValueTask<bool> CheckStreamAccess(ClaimsPrincipal cp, Operation operation, PolicyInformation policy,
		EvaluationContext context, string? streamId) {
		if (cp.Identity is null || !cp.Identity.IsAuthenticated) {
			context.Add(new AssertionMatch(policy,
				new AssertionInformation("unauthenticated", "unauthenticated/anonymous user denied access", Grant.Deny)));
			return true;
		}

		if (streamId is null) {
			context.Add(new AssertionMatch(policy,
				new AssertionInformation("streamId", "streamId is null", Grant.Deny)));
			return true;
		}

		if (streamId == string.Empty)
			streamId = SystemStreams.AllStream;
		if (streamId == SystemStreams.AllStream &&
		    (operation == Operations.Streams.Delete || operation == Operations.Streams.Write)) {
			context.Add(new AssertionMatch(policy,
				new AssertionInformation("streamId", $"{operation.Action} denied on $all", Grant.Deny)));
			return true;
		}

		var action = operation.Action;
		if (SystemStreams.IsMetastream(streamId)) {
			action = operation.Action switch {
				"read" => "metadataRead",
				"write" => "metadataWrite",
				_ => null
			};
			if (action == null)
				return InvalidMetadataOperation(operation, policy, context);
			streamId = SystemStreams.OriginalStreamOf(streamId);
		}

		return action switch {
			"read" => await Check(cp, operation, action, streamId, policy, context),
			"write" => await Check(cp, operation, action, streamId, policy, context),
			"delete" => await Check(cp, operation, action, streamId, policy, context),
			"metadataWrite" => await Check(cp, operation, action, streamId, policy, context),
			"metadataRead" => await Check(cp, operation, action, streamId, policy, context),
			_ => throw new ArgumentOutOfRangeException(nameof(operation.Action), action)
		};
	}

	private async ValueTask<bool> Check(ClaimsPrincipal cp, Operation operation, string action,
		string streamId, PolicyInformation policy, EvaluationContext context) {
		if (await IsSystemOrAdmin(cp, operation, policy, context)) {
			return true;
		}

		var permissions = getAccessPolicyForStream(streamId);

		var roles = action switch {
			"read" => permissions.Readers,
			"write" => permissions.Writers,
			"delete" => permissions.Deleters,
			"metadataRead" => permissions.MetadataReaders,
			"metadataWrite" => permissions.MetadataWriters,
			_ => []
		};

		var isOpsUser = IsOpsUser(cp);
		var isPublicStream = roles.Any(x => x == SystemRoles.All);
		if (isPublicStream && !isOpsUser) {
			context.Add(new AssertionMatch(policy, new AssertionInformation("stream prefix", "public stream", Grant.Allow)));
			return true;
		}

		for (int i = 0; i < roles.Length; i++) {
			var role = roles[i];
			if (cp.FindFirst(x => (x.Type == ClaimTypes.Name || x.Type == ClaimTypes.Role) && x.Value == role)
				is { } matched) {
				context.Add(new AssertionMatch(policy, new AssertionInformation("role match", role, Grant.Allow),
					matched));
				return true;
			}
		}

		if (isPublicStream && isOpsUser) {
			context.Add(new AssertionMatch(policy,
				new AssertionInformation("$ops user denied access", "public stream", Grant.Deny)));
			return true;
		}

		context.Add(new AssertionMatch(policy, new AssertionInformation("denied access", "stream prefix", Grant.Deny)));
		return true;
	}

	private async ValueTask<bool> IsSystemOrAdmin(ClaimsPrincipal cp, Operation operation,
		PolicyInformation policy, EvaluationContext context) {
		return await WellKnownAssertions.System.Evaluate(cp, operation, policy, context)
		       || await WellKnownAssertions.Admin.Evaluate(cp, operation, policy, context);
	}

	private bool IsOpsUser(ClaimsPrincipal cp) {
		var roles = cp.FindAll(x => x.Type == ClaimTypes.Role).ToArray();
		return roles.Length > 0
		       && roles.All(x => x.Value == SystemRoles.Operations);
	}

	private string? FindStreamId(ReadOnlySpan<Parameter> parameters) {
		for (int i = 0; i < parameters.Length; i++) {
			if (parameters[i].Name == "streamId")
				return parameters[i].Value;
		}
		return null;
	}

	private bool InvalidMetadataOperation(Operation operation, PolicyInformation policy,
		EvaluationContext result) {
		result.Add(new AssertionMatch(policy,
			new AssertionInformation("metadata", $"invalid metadata operation {operation.Action}",
				Grant.Deny)));
		return true;
	}
}

