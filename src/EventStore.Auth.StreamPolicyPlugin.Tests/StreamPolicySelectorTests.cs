// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Security.Claims;
using DotNext.Threading;
using EventStore.Auth.StreamPolicyPlugin.Schema;
using EventStore.Core.Authorization;
using EventStore.Core.Bus;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Services;
using EventStore.Core.Tests;
using EventStore.Core.TransactionLog.LogRecords;
using EventStore.Plugins.Authorization;
using Xunit;

namespace EventStore.Auth.StreamPolicyPlugin.Tests;

public class StreamPolicySelectorTests {
	private readonly SynchronousScheduler _bus = new("testBus");
	private const string _userStream = "user-stream";
	private const string _systemStream = "$system-stream";

	private const string _customUser = "ouro";
	private const string _customRole = "user-role";
	private readonly CancellationTokenSource _cts = new ();

	private readonly string[] _projectionStreamPrefixes = ["$et-", "$ce-,", "$bc-", "$category-", "$streams"];

	private static string _customPolicyName => StreamPolicySelector.PolicyName;
	private static string _customPolicyFallbackName => $"{StreamPolicySelector.PolicyName}-notready";

	public static IEnumerable<object[]> ValidActionsData => PolicyTestHelpers.ValidActions.Select(x => new[] { (object)x });

	private Schema.AccessPolicy AdminOnly => new() {
		Readers = [SystemRoles.Admins],
		Writers = [SystemRoles.Admins],
		Deleters = [SystemRoles.Admins],
		MetadataReaders = [SystemRoles.Admins],
		MetadataWriters = [SystemRoles.Admins]
	};

	private StreamPolicySelector CreateSut() {
		return new StreamPolicySelector(_bus, _cts.Token);
	}

	[Theory]
	[InlineData(_systemStream, SystemUsers.Admin, SystemRoles.Admins, Grant.Allow)]
	[InlineData(_userStream, SystemUsers.Admin, SystemRoles.Admins, Grant.Allow)]
	[InlineData(_systemStream, SystemUsers.Operations, SystemRoles.Operations, Grant.Deny)]
	[InlineData(_userStream, SystemUsers.Operations, SystemRoles.Operations, Grant.Deny)]
	[InlineData(_systemStream, _customUser, _customRole, Grant.Deny)]
	[InlineData(_userStream, _customUser, _customRole, Grant.Deny)]
	public async Task when_policy_selector_is_not_ready_only_admin_users_have_access(
		string stream, string username, string role, Grant expectedGrant) {
		var sut = CreateSut();
		var user = PolicyTestHelpers.CreateUser(username, [role]);
		var policy = sut.Select();
		Assert.Equal(_customPolicyFallbackName, policy.Information.Name);

		await AssertAllActionsOnPolicy(policy, user, stream, expectedGrant);
	}

	[Fact]
	public async Task when_policy_stream_is_empty_policy_selector_uses_not_ready_policy() {
		var tcs = new TaskCompletionSource();
		_bus.Subscribe(new AdHocHandler<ClientMessage.ReadStreamEventsBackward>(
			msg => {
				msg.Envelope.ReplyWith(NoStreamResponse(msg));
				tcs.SetResult();
			}));
		var sut = CreateSut();
		await tcs.Task.WithTimeout();
		await Task.Delay(100); // Wait for the policy to appear in the selector

		var policy = sut.Select();
		Assert.Equal(_customPolicyFallbackName, policy.Information.Name);
	}

	[Fact]
	public async Task when_policy_stream_contains_only_invalid_policy_events_policy_selector_uses_not_ready_policy() {
		var tcs = new TaskCompletionSource();
		_bus.Subscribe(new AdHocHandler<ClientMessage.ReadStreamEventsBackward>(
			msg => {
				msg.Envelope.ReplyWith(CreateReadCompleted(msg, [
					CreateResolvedEvent("invalid", "invalid"u8.ToArray(), 0),
					CreateResolvedEvent(StreamPolicySelector.PolicyEventType, "invalid"u8.ToArray(), 1),
					CreateResolvedEvent("invalid", PolicyTestHelpers.SerializePolicy(StreamPolicySelector.DefaultPolicy),
						2)
				]));
				tcs.SetResult();
			}));
		var sut = CreateSut();
		await tcs.Task.WithTimeout();
		await Task.Delay(100); // Wait for the policy to appear in the selector

		var policy = sut.Select();
		Assert.Equal(_customPolicyFallbackName, policy.Information.Name);
	}

	[Fact]
	public async Task when_parsing_policy_with_multiple_rules_the_rules_are_applied_in_order() {
		string username = _customUser;
		string role = _customRole;
		var tcs = new TaskCompletionSource();
		var existingPolicy = new Schema.Policy {
			StreamPolicies = new Dictionary<string, Schema.AccessPolicy>{
				{ "systemDefault", new Schema.AccessPolicy{ Readers = [], Writers = [], Deleters = [], MetadataWriters = [], MetadataReaders = []}},
				{ "userDefault", new Schema.AccessPolicy{ Readers = [role], Writers = [role], Deleters = [role], MetadataWriters = [], MetadataReaders = []}},
				{ "customOne", new Schema.AccessPolicy{ Readers = [role], Writers = [], Deleters = [], MetadataWriters = [], MetadataReaders = [] }},
				{ "customTwo", new Schema.AccessPolicy{ Readers = [role], Writers = [role], Deleters = [], MetadataWriters = [], MetadataReaders = [] }},
			},
			DefaultStreamRules = new Schema.DefaultStreamRules{ SystemStreams = "systemDefault", UserStreams = "userDefault"},
			StreamRules = new Schema.StreamRule[] {
				new StreamRule{ Policy = "customOne", StartsWith = "account-"},
				new StreamRule {Policy = "customTwo", StartsWith = "account"}
			}
		};

		var user = PolicyTestHelpers.CreateUser(username, [role]);

		_bus.Subscribe(new AdHocHandler<ClientMessage.ReadStreamEventsBackward>(
			msg => {
				msg.Envelope.ReplyWith(CreateReadCompleted(msg, [CreateResolvedEventForPolicy(existingPolicy, 0)]));
				tcs.SetResult();
			}));
		var sut = CreateSut();
		await tcs.Task.WithTimeout();
		await Task.Delay(100); // Wait for the policy to appear in the selector

		var policy = sut.Select();

		await AssertAllowedStreamActionsOnPolicy(policy, user, "account-1", ["read"]);
		await AssertAllowedSubscriptionActionsOnPolicy(policy, user, "account-1", ["read"]);

		await AssertAllowedStreamActionsOnPolicy(policy, user, "account1", ["read", "write"]);
		await AssertAllowedSubscriptionActionsOnPolicy(policy, user, "account1", ["read", "write"]);

		await AssertAllowedStreamActionsOnPolicy(policy, user, "acc-3", ["read", "write", "delete"]);
		await AssertAllowedSubscriptionActionsOnPolicy(policy, user, "acc-3", ["read", "write", "delete"]);

		await AssertAllowedStreamActionsOnPolicy(policy, user, "$account", []);
		await AssertAllowedSubscriptionActionsOnPolicy(policy, user, "$account", []);
	}

	[Theory]
	[InlineData(SystemUsers.Admin, SystemRoles.Admins, Grant.Allow, Grant.Allow)]
	[InlineData(SystemUsers.Operations, SystemRoles.Operations, Grant.Deny, Grant.Deny)]
	[InlineData(_customUser, _customRole, Grant.Allow, Grant.Deny)]
	public async Task when_using_the_default_policy_standard_projection_streams_are_read_only(
		string username, string role, Grant expectedReadGrant, Grant expectedWriteGrant) {
		var tcs = new TaskCompletionSource();
		var existingPolicy = StreamPolicySelector.DefaultPolicy;
		var user = PolicyTestHelpers.CreateUser(username, [role]);

		_bus.Subscribe(new AdHocHandler<ClientMessage.ReadStreamEventsBackward>(
			msg => {
				msg.Envelope.ReplyWith(CreateReadCompleted(msg, [CreateResolvedEventForPolicy(existingPolicy, 0)]));
				tcs.SetResult();
			}));
		var sut = CreateSut();
		await tcs.Task.WithTimeout();
		await Task.Delay(100); // Wait for the policy to appear in the selector

		var policy = sut.Select();
		Assert.Equal(_customPolicyName, policy.Information.Name);
		string[] allowedActions;
		if (expectedReadGrant == Grant.Allow) {
			allowedActions = expectedWriteGrant == Grant.Allow
				? PolicyTestHelpers.ValidActions
				: ["read", "metadataRead"];
		} else {
			allowedActions = [];
		}

		foreach (var prefix in _projectionStreamPrefixes) {
			var stream = $"{prefix}test";
			await AssertAllowedStreamActionsOnPolicy(policy, user, stream, allowedActions);
			await AssertAllowedSubscriptionActionsOnPolicy(policy, user, stream, allowedActions);
		}
	}

	[Theory]
	[InlineData(_systemStream, SystemUsers.Admin, SystemRoles.Admins, Grant.Allow)]
	[InlineData(_userStream, SystemUsers.Admin, SystemRoles.Admins, Grant.Allow)]
	[InlineData(_systemStream, SystemUsers.Operations, SystemRoles.Operations, Grant.Deny)]
	[InlineData(_userStream, SystemUsers.Operations, SystemRoles.Operations, Grant.Deny)]
	[InlineData(_systemStream, _customUser, _customRole, Grant.Deny)]
	[InlineData(_userStream, _customUser, _customRole, Grant.Allow)]
	public async Task when_policy_stream_contains_a_valid_policy_that_policy_is_used(
		string stream, string username, string role, Grant expectedGrant) {
		var tcs = new TaskCompletionSource();
		var existingPolicy = StreamPolicySelector.DefaultPolicy;
		var user = PolicyTestHelpers.CreateUser(username, [role]);

		_bus.Subscribe(new AdHocHandler<ClientMessage.ReadStreamEventsBackward>(
			msg => {
				msg.Envelope.ReplyWith(CreateReadCompleted(msg, [CreateResolvedEventForPolicy(existingPolicy, 0)]));
				tcs.SetResult();
			}));
		var sut = CreateSut();
		await tcs.Task.WithTimeout();
		await Task.Delay(100); // Wait for the policy to appear in the selector

		var policy = sut.Select();
		Assert.Equal(_customPolicyName, policy.Information.Name);

		await AssertAllActionsOnPolicy(policy, user, stream, expectedGrant);
	}

	[Theory]
	[MemberData(nameof(ValidActionsData))]
	public async Task
		when_policy_stream_contains_multiple_policies_the_most_recent_policy_is_used(string allowedAction) {
		var tcs = new TaskCompletionSource();
		var user = PolicyTestHelpers.CreateUser(SystemUsers.Operations, [SystemRoles.Operations]);
		var stream = _systemStream;

		var policy1 = StreamPolicySelector.DefaultPolicy;
		var policy2 = new Schema.Policy {
			StreamPolicies = new Dictionary<string, Schema.AccessPolicy> {
				{ "NewPolicy", PolicyTestHelpers.CreateAccessPolicyDtoForAction(allowedAction, SystemUsers.Operations) },
				{ "DefaultPolicy", AdminOnly }
			},
			DefaultStreamRules = new Schema.DefaultStreamRules
				{ SystemStreams = "DefaultPolicy", UserStreams = "DefaultPolicy" },
			StreamRules = [new Schema.StreamRule { Policy = "NewPolicy", StartsWith = stream }]
		};

		_bus.Subscribe(new AdHocHandler<ClientMessage.ReadStreamEventsBackward>(
			msg => {
				msg.Envelope.ReplyWith(CreateReadCompleted(msg, [
					CreateResolvedEventForPolicy(policy2, 1),
					CreateResolvedEventForPolicy(policy1, 0)
				]));
				tcs.SetResult();
			}));
		var sut = CreateSut();
		await tcs.Task.WithTimeout();
		await Task.Delay(100); // Wait for the policy to appear in the selector

		var policy = sut.Select();
		Assert.Equal(_customPolicyName, policy.Information.Name);

		await AssertAllowedStreamActionsOnPolicy(policy, user, stream, [allowedAction]);
		await AssertAllowedSubscriptionActionsOnPolicy(policy, user, stream, [allowedAction]);
	}

	[Theory]
	[MemberData(nameof(ValidActionsData))]
	public async Task when_policy_stream_contains_an_invalid_policy_the_most_recent_valid_policy_is_used(
		string allowedAction) {
		var tcs = new TaskCompletionSource();
		var user = PolicyTestHelpers.CreateUser(SystemUsers.Operations, [SystemRoles.Operations]);
		var stream = _systemStream;

		var policy1 = StreamPolicySelector.DefaultPolicy;
		var policy2 = new Schema.Policy {
			StreamPolicies = new Dictionary<string, Schema.AccessPolicy> {
				{ "NewPolicy", PolicyTestHelpers.CreateAccessPolicyDtoForAction(allowedAction, SystemUsers.Operations) },
				{ "DefaultPolicy", AdminOnly }
			},
			DefaultStreamRules = new Schema.DefaultStreamRules
				{ SystemStreams = "DefaultPolicy", UserStreams = "DefaultPolicy" },
			StreamRules = [new Schema.StreamRule { Policy = "NewPolicy", StartsWith = stream }]
		};

		_bus.Subscribe(new AdHocHandler<ClientMessage.ReadStreamEventsBackward>(
			msg => {
				msg.Envelope.ReplyWith(CreateReadCompleted(msg, [
					CreateResolvedEvent("invalid", "invalid"u8.ToArray(), 2),
					CreateResolvedEventForPolicy(policy2, 1),
					CreateResolvedEventForPolicy(policy1, 0)
				]));
				tcs.SetResult();
			}));
		var sut = CreateSut();
		await tcs.Task.WithTimeout();
		await Task.Delay(100); // Wait for the policy to appear in the selector

		var policy = sut.Select();
		Assert.Equal(_customPolicyName, policy.Information.Name);

		await AssertAllowedStreamActionsOnPolicy(policy, user, stream, [allowedAction]);
		await AssertAllowedSubscriptionActionsOnPolicy(policy, user, stream, [allowedAction]);
	}

	[Theory]
	[MemberData(nameof(ValidActionsData))]
	public async Task when_a_new_policy_event_is_written_the_new_policy_is_applied(string allowedAction) {
		var user = PolicyTestHelpers.CreateUser(SystemUsers.Operations, [SystemRoles.Operations]);
		var stream = _systemStream;

		var policy1 = StreamPolicySelector.DefaultPolicy;
		var policy2 = new Schema.Policy {
			StreamPolicies = new Dictionary<string, Schema.AccessPolicy> {
				{ "NewPolicy", PolicyTestHelpers.CreateAccessPolicyDtoForAction(allowedAction, SystemUsers.Operations)},
				{ "DefaultPolicy", AdminOnly }
			},
			DefaultStreamRules = new Schema.DefaultStreamRules{ SystemStreams = "DefaultPolicy", UserStreams = "DefaultPolicy"},
			StreamRules = [new Schema.StreamRule{ Policy = "NewPolicy", StartsWith = stream}]
		};

		var subscribeSource = new TaskCompletionSource<ClientMessage.SubscribeToStream>();
		_bus.Subscribe(new AdHocHandler<ClientMessage.SubscribeToStream>(msg => {
			subscribeSource.TrySetResult(msg);
		}));

		var readCompletionSource = new TaskCompletionSource();
		_bus.Subscribe(new AdHocHandler<ClientMessage.ReadStreamEventsBackward>(
			msg => {
				msg.Envelope.ReplyWith(CreateReadCompleted(msg, [CreateResolvedEventForPolicy(policy1, 0)]));
				readCompletionSource.SetResult();
			}));
		var sut = CreateSut();
		await readCompletionSource.Task.WithTimeout();
		await Task.Delay(100); // Wait for the policy to appear in the selector

		var initialPolicy = sut.Select();
		Assert.Equal(_customPolicyName, initialPolicy.Information.Name);

		// Test that the old policy is in effect
		await AssertAllActionsOnPolicy(initialPolicy, user, stream, Grant.Deny);

		// Write a new policy event
		var subscribeMsg = await subscribeSource.Task.WithTimeout();
		subscribeMsg.Envelope.ReplyWith(new ClientMessage.SubscriptionConfirmation(subscribeMsg.CorrelationId, 0, 0));
		subscribeMsg.Envelope.ReplyWith(new ClientMessage.StreamEventAppeared(
			subscribeMsg.CorrelationId, CreateResolvedEventForPolicy(policy2, 1)));
		await Task.Delay(100); // Wait for the policy to appear in the selector

		var newPolicy = sut.Select();
		Assert.Equal(_customPolicyName, newPolicy.Information.Name);
		await AssertAllowedStreamActionsOnPolicy(newPolicy, user, stream, [allowedAction]);
		await AssertAllowedSubscriptionActionsOnPolicy(newPolicy, user, stream, [allowedAction]);
	}

	[Theory]
	[MemberData(nameof(ValidActionsData))]
	public async Task when_an_invalid_policy_event_is_written_the_previous_policy_is_used(string allowedAction) {
		var user = PolicyTestHelpers.CreateUser(SystemUsers.Operations, [SystemRoles.Operations]);
		var stream = _systemStream;

		var policy1 = new Schema.Policy {
			StreamPolicies = new Dictionary<string, Schema.AccessPolicy> {
				{ "NewPolicy", PolicyTestHelpers.CreateAccessPolicyDtoForAction(allowedAction, SystemUsers.Operations)},
				{ "DefaultPolicy", AdminOnly }
			},
			DefaultStreamRules = new Schema.DefaultStreamRules{ SystemStreams = "DefaultPolicy", UserStreams = "DefaultPolicy"},
			StreamRules = [new Schema.StreamRule{ Policy = "NewPolicy", StartsWith = stream}]
		};

		var subscribeSource = new TaskCompletionSource<ClientMessage.SubscribeToStream>();
		_bus.Subscribe(new AdHocHandler<ClientMessage.SubscribeToStream>(msg => {
			subscribeSource.TrySetResult(msg);
		}));

		var readCompletionSource = new TaskCompletionSource();
		_bus.Subscribe(new AdHocHandler<ClientMessage.ReadStreamEventsBackward>(
			msg => {
				msg.Envelope.ReplyWith(CreateReadCompleted(msg, [CreateResolvedEventForPolicy(policy1, 0)]));
				readCompletionSource.SetResult();
			}));
		var sut = CreateSut();
		await readCompletionSource.Task.WithTimeout();
		await Task.Delay(100); // Wait for the policy to appear in the selector

		var initialPolicy = sut.Select();
		Assert.Equal(_customPolicyName, initialPolicy.Information.Name);

		// Write an invalid policy event
		var subscribeMsg = await subscribeSource.Task.WithTimeout();
		subscribeMsg.Envelope.ReplyWith(new ClientMessage.SubscriptionConfirmation(subscribeMsg.CorrelationId, 0, 0));
		subscribeMsg.Envelope.ReplyWith(new ClientMessage.StreamEventAppeared(
			subscribeMsg.CorrelationId, CreateResolvedEvent("invalid", "invalid"u8.ToArray(), 1)));
		await Task.Delay(100); // Wait for the policy to appear in the selector

		var policy = sut.Select();
		Assert.Equal(_customPolicyName, policy.Information.Name);

		await AssertAllowedStreamActionsOnPolicy(policy, user, stream, [allowedAction]);
		await AssertAllowedSubscriptionActionsOnPolicy(policy, user, stream, [allowedAction]);
	}

	[Theory]
	[InlineData(ClientMessage.NotHandled.Types.NotHandledReason.NotReady)]
	[InlineData(ClientMessage.NotHandled.Types.NotHandledReason.TooBusy)]
	public async Task when_the_policy_subscription_is_not_handled_it_is_retried_when_recoverable(
		ClientMessage.NotHandled.Types.NotHandledReason notHandledReason) {
		var subscriptionMessages = new List<ClientMessage.SubscribeToStream>();
		var subscribeEvent = new ManualResetEvent(false);
		_bus.Subscribe(new AdHocHandler<ClientMessage.SubscribeToStream>(msg => {
			subscriptionMessages.Add(msg);
			subscribeEvent.Set();
		}));
		// Respond to the initial read with empty stream
		_bus.Subscribe(new AdHocHandler<ClientMessage.ReadStreamEventsBackward>(
			msg => {
				msg.Envelope.ReplyWith(NoStreamResponse(msg));
			}));

		var sut = CreateSut();
		await subscribeEvent.WaitAsync().AsTask().WithTimeout();
		subscribeEvent.Reset();

		var initialSubscription = subscriptionMessages.FirstOrDefault();
		Assert.NotNull(initialSubscription);

		// Fail the subscription attempt
		initialSubscription.Envelope.ReplyWith(
			new ClientMessage.NotHandled(initialSubscription.CorrelationId, notHandledReason, ""));

		// It should attempt to resubscribe
		await subscribeEvent.WaitAsync().AsTask().WithTimeout();
		Assert.Equal(2, subscriptionMessages.Count);
	}

	private static async Task AssertAllActionsOnPolicy(
		ReadOnlyPolicy policy, ClaimsPrincipal user, string stream, Grant expectedGrant) {
		var allowedActions = expectedGrant == Grant.Allow ? PolicyTestHelpers.ValidActions : [];
		await AssertAllowedStreamActionsOnPolicy(policy, user, stream, allowedActions);
		await AssertAllowedSubscriptionActionsOnPolicy(policy, user, stream, allowedActions);
	}

	private static async Task AssertAllowedStreamActionsOnPolicy(
		ReadOnlyPolicy policy, ClaimsPrincipal user, string stream, string[] allowedActions) {
		foreach (var action in PolicyTestHelpers.ValidActions) {
			var operationDefinition = PolicyTestHelpers.ActionToOperationDefinition(action);
			var operation = new Operation(operationDefinition)
				.WithParameter(Operations.Streams.Parameters.StreamId(stream));
			Assert.True(policy.TryGetAssertions(operationDefinition, out var assertions));
			Assert.Single(assertions.ToArray());

			var assertion = assertions.ToArray()[0];
			var evaluationContext = new EvaluationContext(operation, CancellationToken.None);
			await assertion.Evaluate(user, operation, policy.Information, evaluationContext);
			Assert.Equal(allowedActions.Contains(action) ? Grant.Allow : Grant.Deny, evaluationContext.Grant);
		}
	}

	private static async Task AssertAllowedSubscriptionActionsOnPolicy(
		ReadOnlyPolicy policy, ClaimsPrincipal user, string stream, string[] allowedActions) {
		var operationDefinition = Operations.Subscriptions.ProcessMessages;
		var operation = new Operation(operationDefinition)
			.WithParameter(Operations.Subscriptions.Parameters.StreamId(stream));
		Assert.True(policy.TryGetAssertions(operationDefinition, out var assertions));
		Assert.Single(assertions.ToArray());

		var assertion = assertions.ToArray()[0];
		var evaluationContext = new EvaluationContext(operation, CancellationToken.None);
		await assertion.Evaluate(user, operation, policy.Information, evaluationContext);
		Assert.Equal(allowedActions.Contains("read") ? Grant.Allow : Grant.Deny, evaluationContext.Grant);
	}

	private static ClientMessage.ReadStreamEventsBackwardCompleted
		NoStreamResponse(ClientMessage.ReadStreamEventsBackward msg) =>
		new(
			msg.CorrelationId, msg.EventStreamId, msg.FromEventNumber, msg.MaxCount,
			ReadStreamResult.NoStream, [], StreamMetadata.Empty, true, "",
			msg.FromEventNumber, msg.FromEventNumber, true, 0L);

	private ResolvedEvent CreateResolvedEventForPolicy(Schema.Policy policy, long eventNumber) {
		var data = PolicyTestHelpers.SerializePolicy(policy);
		return CreateResolvedEvent(StreamPolicySelector.PolicyEventType, data, eventNumber);
	}

	private ResolvedEvent CreateResolvedEvent(string eventType, byte[] data, long eventNumber) {
		var logPosition = eventNumber * 100;
		var prepare = new PrepareLogRecord(logPosition, Guid.NewGuid(), Guid.NewGuid(),
			transactionPosition: logPosition,
			transactionOffset: 0,
			StreamPolicySelector.PolicyStream,
			eventStreamIdSize: null,
			expectedVersion: eventNumber, DateTime.Now, PrepareFlags.Data,
			eventType, eventTypeSize: null, data, ReadOnlyMemory<byte>.Empty);
		var eventRecord = new EventRecord(eventNumber, prepare, StreamPolicySelector.PolicyStream, eventType);
		return ResolvedEvent.ForUnresolvedEvent(eventRecord, logPosition);
	}

	private ClientMessage.ReadStreamEventsBackwardCompleted CreateReadCompleted(
		ClientMessage.ReadStreamEventsBackward msg, ResolvedEvent[] events) =>
		new(msg.CorrelationId, msg.EventStreamId, msg.FromEventNumber, msg.MaxCount,
			ReadStreamResult.Success, events, StreamMetadata.Empty, true, "",
			msg.FromEventNumber, msg.FromEventNumber, true, 1000L);
}
