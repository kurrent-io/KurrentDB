// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Bus;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Services;
using EventStore.Plugins.Authorization;

namespace EventStore.Core.Authorization;

public class
	LegacyStreamPermissionAssertion : IStreamPermissionAssertion {
	private readonly IPublisher _publisher;

	public LegacyStreamPermissionAssertion(IPublisher publisher) {
		_publisher = publisher;
	}

	public AssertionInformation Information { get; } =
		new AssertionInformation("stream", "legacy acl", Grant.Unknown);

	public Grant Grant { get; } = Grant.Unknown;

	public ValueTask<bool> Evaluate(ClaimsPrincipal cp, Operation operation, PolicyInformation policy,
		EvaluationContext context) {
		var streamId = FindStreamId(operation.Parameters.Span, context.CancellationToken);

		if (streamId.IsCompleted)
			return CheckStreamAccess(cp, operation, policy, context, streamId.Result);
		return CheckStreamAccessAsync(streamId, cp, operation, policy, context);
	}

	private async ValueTask<bool> CheckStreamAccessAsync(ValueTask<string> pending, ClaimsPrincipal cp,
		Operation operation, PolicyInformation policy, EvaluationContext result) {
		var streamId = await pending;
		return await CheckStreamAccess(cp, operation, policy, result, streamId);
	}

	private ValueTask<bool> CheckStreamAccess(ClaimsPrincipal cp, Operation operation, PolicyInformation policy,
		EvaluationContext context, string streamId) {
		if (streamId == null) {
			context.Add(new AssertionMatch(policy,
				new AssertionInformation("streamId", "streamId is null", Grant.Deny)));
			return new ValueTask<bool>(true);
		}

		if (streamId == "")
			streamId = SystemStreams.AllStream;
		if (streamId == SystemStreams.AllStream &&
		    (operation == Operations.Streams.Delete || operation == Operations.Streams.Write)) {
			context.Add(new AssertionMatch(policy,
				new AssertionInformation("streamId", $"{operation.Action} denied on $all", Grant.Deny)));
			return new ValueTask<bool>(true);
		}

		var action = operation.Action;
		if (SystemStreams.IsMetastream(streamId)) {
			action = operation.Action switch {
				"read" => "metadataRead",
				"write" => "metadataWrite",
				_ => null
			};
			streamId = SystemStreams.OriginalStreamOf(streamId);
		}

		return action switch {
			"read" => Check(cp, operation, action, streamId, policy, context),
			"write" => Check(cp, operation, action, streamId, policy, context),
			"delete" => Check(cp, operation, action, streamId, policy, context),
			"metadataWrite" => Check(cp, operation, action, streamId, policy, context),
			"metadataRead" => Check(cp, operation, action, streamId, policy, context),
			null => InvalidMetadataOperation(operation, policy, context),
			_ => throw new ArgumentOutOfRangeException(nameof(operation.Action), action)
		};
	}

	private ValueTask<bool> Check(ClaimsPrincipal cp, Operation operation, string action, string streamId,
		PolicyInformation policy, EvaluationContext context) {
#pragma warning disable CA2012
		var preChecks = IsSystemOrAdmin(cp, operation, policy, context);
#pragma warning restore CA2012
		if (preChecks.IsCompleted && preChecks.Result) return preChecks;

		return CheckAsync(preChecks, cp, action, streamId, policy, context);
	}

	private ValueTask<bool> IsSystemOrAdmin(ClaimsPrincipal cp, Operation operation,
		PolicyInformation policy, EvaluationContext context) {
		var isSystem = WellKnownAssertions.System.Evaluate(cp, operation, policy, context);
		if (isSystem.IsCompleted) {
			if (isSystem.Result)
				return isSystem;
			return WellKnownAssertions.Admin.Evaluate(cp, operation, policy, context);
		}

		// This should never be run, but is required for the compilation to be reasonable
		return IsSystemOrAdminAsync(isSystem, cp, operation, policy, context);
	}

	private async ValueTask<bool> IsSystemOrAdminAsync(ValueTask<bool> isSystem, ClaimsPrincipal cp,
		Operation operation,
		PolicyInformation policy, EvaluationContext context) {
		if (await isSystem) return true;

		return await WellKnownAssertions.Admin.Evaluate(cp, operation, policy, context);
	}

	private async ValueTask<bool> CheckAsync(ValueTask<bool> preChecks, ClaimsPrincipal cp, string action,
		string streamId, PolicyInformation policy, EvaluationContext context) {
		var isSystemOrAdmin = await preChecks;
		if (isSystemOrAdmin)
			return true;
		var acl = await StorageMessage.EffectiveAcl.LoadAsync(_publisher, streamId, context.CancellationToken);
		var roles = RolesFor(action, acl);
		if (roles.Any(x => x == SystemRoles.All)) {
			context.Add(new AssertionMatch(policy,
				new AssertionInformation("stream", "public stream", Grant.Allow)));
			return true;
		}

		for (int i = 0; i < roles.Length; i++) {
			var role = roles[i];
			if (cp.FindFirst(x => (x.Type == ClaimTypes.Name || x.Type == ClaimTypes.Role) && x.Value == role)
			    is Claim matched) {
				context.Add(new AssertionMatch(policy, new AssertionInformation("role match", role, Grant.Allow),
					matched));
				return true;
			}
		}

		return false;
	}

	private ValueTask<bool> InvalidMetadataOperation(Operation operation, PolicyInformation policy,
		EvaluationContext result) {
		result.Add(new AssertionMatch(policy,
			new AssertionInformation("metadata", $"invalid metadata operation {operation.Action}",
				Grant.Deny)));
		return new ValueTask<bool>(true);
	}

	private ValueTask<string> FindStreamId(ReadOnlySpan<Parameter> parameters,
		CancellationToken cancellationToken) {
		string transactionId = null;
		for (int i = 0; i < parameters.Length; i++) {
			if (parameters[i].Name == "streamId")
				return new ValueTask<string>(parameters[i].Value);
			if (parameters[i].Name == "transactionId")
				transactionId = parameters[i].Value;
		}

		if (transactionId != null) return FindStreamFromTransactionId(long.Parse(transactionId), cancellationToken);
		return new ValueTask<string>((string)null);
	}

	private ValueTask<string> FindStreamFromTransactionId(long transactionId, CancellationToken cancellationToken) {
		var envelope = new StreamIdFromTransactionIdEnvelope();
		_publisher.Publish(
			new StorageMessage.StreamIdFromTransactionIdRequest(transactionId, envelope, cancellationToken));
		return new ValueTask<string>(envelope.Task);
	}

	private string[] RolesFor(string action, StorageMessage.EffectiveAcl acl) {
		return action switch {
			"read" => acl.Stream?.ReadRoles ?? acl.System?.ReadRoles ?? acl.Default?.ReadRoles,
			"write" => acl.Stream?.WriteRoles ?? acl.System?.WriteRoles ?? acl.Default?.WriteRoles,
			"delete" => acl.Stream?.DeleteRoles ?? acl.System?.DeleteRoles ?? acl.Default?.DeleteRoles,
			"metadataRead" => acl.Stream?.MetaReadRoles ?? acl.System?.MetaReadRoles ?? acl.Default?.MetaReadRoles,
			"metadataWrite" => acl.Stream?.MetaWriteRoles ??
			                   acl.System?.MetaWriteRoles ?? acl.Default?.MetaWriteRoles,
			_ => Array.Empty<string>()
		};
	}

	private class StreamIdFromTransactionIdEnvelope : IEnvelope {
		private readonly TaskCompletionSource<string> _tcs;

		public StreamIdFromTransactionIdEnvelope() {
			_tcs = new TaskCompletionSource<string>();
		}

		public Task<string> Task => _tcs.Task;

		public void ReplyWith<T>(T message) where T : Message {
			if (message is StorageMessage.StreamIdFromTransactionIdResponse response)
				_tcs.TrySetResult(response.StreamId);
			else if (message is StorageMessage.OperationCancelledMessage cancelled)
				_tcs.TrySetCanceled(cancelled.CancellationToken);
			else
				_tcs.TrySetException(new InvalidOperationException($"Wrong message type {message.GetType()}"));
		}
	}
}
