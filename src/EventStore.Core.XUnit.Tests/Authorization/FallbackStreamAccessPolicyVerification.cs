// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using DotNext.Collections.Generic;
using EventStore.Core.Authorization;
using EventStore.Core.Authorization.AuthorizationPolicies;
using EventStore.Plugins.Authorization;
using Xunit;

namespace EventStore.Core.XUnit.Tests.Authorization;

public class FallbackStreamAccessPolicyVerification {
	private const string StreamId = "test-stream";
	private const string StreamsResource = "streams";
	private const string SubscriptionsResource = "subscriptions";

	private readonly ClaimsPrincipal _claimsPrincipal = new(new ClaimsIdentity(
		new[] { new Claim(ClaimTypes.Name, "test-user") }));

	private MultiPolicyEvaluator CreateSut() {
		var fallback = new FallbackStreamAccessPolicySelector();
		var dummy = new DummyStreamAccess().Create(SampleOperations);
		return new MultiPolicyEvaluator(new StaticAuthorizationPolicyRegistry([fallback, dummy]));
	}

	// This is not an extensive list of all operations
	// Just a sample of the different resources
	private static OperationDefinition[] SampleOperations => [
		Operations.Streams.Read,
		Operations.Streams.Write,
		Operations.Streams.Delete,
		Operations.Streams.MetadataRead,
		Operations.Streams.MetadataWrite,
		Operations.Node.Gossip.Update,
		Operations.Node.Elections.Proposal,
		Operations.Node.MergeIndexes,
		Operations.Node.Login,
		Operations.Node.Ping,
		Operations.Node.Shutdown,
		Operations.Node.Redirect,
		Operations.Projections.List,
		Operations.Projections.Create,
		Operations.Projections.Abort,
		Operations.Projections.Delete,
		Operations.Projections.Restart,
		Operations.Subscriptions.ProcessMessages,
		Operations.Subscriptions.Restart,
		Operations.Subscriptions.Delete,
		Operations.Subscriptions.ReplayParked,
		Operations.Users.CurrentUser,
		Operations.Users.Enable,
		Operations.Users.Read,
		Operations.Users.ChangePassword
	];

	public static TheoryData<OperationDefinition> SubscriptionOperations() {
		var data = new TheoryData<OperationDefinition>();
		SampleOperations.Where(x => x.Resource == SubscriptionsResource)
		.ForEach(x => data.Add(x));

		return data;
	}

	public static TheoryData<OperationDefinition> StreamOperations() {
		var data = new TheoryData<OperationDefinition>();
		SampleOperations.Where(x => x.Resource == StreamsResource)
			.ForEach(x => data.Add(x));

		return data;
	}

	public static TheoryData<OperationDefinition> OtherOperations() {
		var data = new TheoryData<OperationDefinition>();
		SampleOperations.Where(x => x.Resource != StreamsResource && x.Resource != SubscriptionsResource)
			.ForEach(x => data.Add(x));

		return data;
	}

	[Theory]
	[MemberData(nameof(StreamOperations))]
	public async Task restricts_stream_access(OperationDefinition operationDefinition) {
		var sut = CreateSut();

		var operation = new Operation(operationDefinition)
			.WithParameter(Operations.Streams.Parameters.StreamId(StreamId));
		var res = await sut.EvaluateAsync(_claimsPrincipal, operation, CancellationToken.None);

		Assert.Equal(Grant.Deny, res.Grant);
		Assert.Contains("restricted to system or admin users only", res.ToString());
	}

	[Theory]
	[MemberData(nameof(SubscriptionOperations))]
	public async Task restricts_processing_subscription_messages(OperationDefinition operationDefinition) {
		var sut = CreateSut();

		var operation = new Operation(operationDefinition)
			.WithParameter(Operations.Subscriptions.Parameters.StreamId(StreamId));
		var res = await sut.EvaluateAsync(_claimsPrincipal, operation, CancellationToken.None);

		if (operation.Action == Operations.Subscriptions.ProcessMessages.Action) {
			Assert.Equal(Grant.Deny, res.Grant);
			Assert.Contains("restricted to system or admin users only", res.ToString());
		} else {
			Assert.Equal(Grant.Allow, res.Grant);
		}
	}

	[Theory]
	[MemberData(nameof(OtherOperations))]
	public async Task does_not_restrict_other_operations(OperationDefinition operationDefinition) {
		var sut = CreateSut();

		var operation = new Operation(operationDefinition);
		var res = await sut.EvaluateAsync(_claimsPrincipal, operation, CancellationToken.None);

		Assert.Equal(Grant.Allow, res.Grant);
	}

	private class DummyStreamAccess {
		public StaticPolicySelector Create(OperationDefinition[] operations) {
			var policy = new Policy("dummy", 1, DateTimeOffset.MinValue);
			foreach (var op in operations) {
				policy.Add(op, new NonStreamAssertion($"{op.Resource}.{op.Action}"));
			}

			return new StaticPolicySelector(policy.AsReadOnly());
		}

		private class NonStreamAssertion(string resource) : IAssertion {
			public Grant Grant { get; } = Grant.Allow;
			public AssertionInformation Information { get; } = new("dummy", resource, Grant.Allow);

			public ValueTask<bool> Evaluate(ClaimsPrincipal cp, Operation operation, PolicyInformation policy,
				EvaluationContext context) {
				if (operation.Resource.Contains(StreamsResource)) {
					Assert.Fail("Should not assert on stream operations");
				} else {
					context.Add(new AssertionMatch(policy, Information, []));
				}

				return ValueTask.FromResult(true);
			}
		}
	}
}
