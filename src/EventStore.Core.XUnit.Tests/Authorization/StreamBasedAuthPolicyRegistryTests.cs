// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using DotNext.Threading;
using EventStore.Common.Exceptions;
using EventStore.Core.Authorization;
using EventStore.Core.Authorization.AuthorizationPolicies;
using EventStore.Core.Bus;
using EventStore.Core.Messages;
using EventStore.Core.Data;
using EventStore.Core.Messaging;
using EventStore.Core.Services;
using EventStore.Core.TransactionLog.LogRecords;
using Xunit;
using EventRecord = EventStore.Core.Data.EventRecord;
using ResolvedEvent = EventStore.Core.Data.ResolvedEvent;

namespace EventStore.Core.XUnit.Tests.Authorization;

public class StreamBasedAuthPolicyRegistryTests {
	private const string AclsPolicy = LegacyPolicySelectorFactory.LegacyPolicySelectorName;
	private const string FallbackPolicy = FallbackStreamAccessPolicySelector.FallbackPolicyName;

	private List<ReadOnlyPolicy[]> _policyUpdates = new();
	private AsyncManualResetEvent _policyUpdated = new(false);
	private TaskCompletionSource<ClientMessage.SubscribeToStream> _subscribed = new();

	private static readonly JsonSerializerOptions SerializerOptions = new()
		{ PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
	private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(1);

	private StreamBasedAuthorizationPolicyRegistry CreateSut(
		FakePolicySelectorPlugin[] plugins, AuthorizationPolicySettings defaultSettings, Action<ClientMessage.ReadStreamEventsBackward> onReadBackwards) {
		var publisher = new AuthRegistryTestPublisher(onReadBackwards);
		_subscribed = publisher.Subscribed;
		_policyUpdated = new AsyncManualResetEvent(false);
		_policyUpdates = [];

		var legacySelector = new LegacyPolicySelectorFactory(false, false, false).Create(publisher);
		// ReSharper disable once CoVariantArrayConversion
		var sut = new StreamBasedAuthorizationPolicyRegistry(publisher, legacySelector, plugins, defaultSettings);
		sut.PolicyChanged += (_, args) => {
			_policyUpdates.Add(args.EffectivePolicies);
			_policyUpdated.Set();
		};
		return sut;
	}

	public static TheoryData<DefaultSettingsTestCase> DefaultSettingsCases => new() {
		new DefaultSettingsTestCase(AclsPolicy, new AuthorizationPolicySettings(AclsPolicy), [AclsPolicy], null),
		new DefaultSettingsTestCase(FallbackPolicy, new AuthorizationPolicySettings(FallbackPolicy), [FallbackPolicy, AclsPolicy], null),
		new DefaultSettingsTestCase("valid", new AuthorizationPolicySettings("valid"), ["valid", AclsPolicy], new FakePolicySelectorPlugin("valid", true)),
		new DefaultSettingsTestCase("not found", new AuthorizationPolicySettings("not found"), [FallbackPolicy, AclsPolicy], new FakePolicySelectorPlugin("valid", true)),
		new DefaultSettingsTestCase("disabled", new AuthorizationPolicySettings("disabled"), [FallbackPolicy, AclsPolicy], new FakePolicySelectorPlugin("disabled", false)),
	};

	[Theory]
	[MemberData(nameof(DefaultSettingsCases))]
	public async Task when_starting_with_no_existing_settings_events_the_default_is_used(DefaultSettingsTestCase testCase) {
		var sut = CreateSut(
			testCase.CustomPlugin is null ? [] : [testCase.CustomPlugin], testCase.DefaultSettings,
			m => {
				m.Envelope.ReplyWith(ErrorResponse(m, ReadStreamResult.NoStream));
			});

		await sut.Start();
		Assert.True(await _policyUpdated.WaitAsync(Timeout));

		// The expected policies are applied
		Assert.Equal(testCase.AppliesPolicies, sut.EffectivePolicies.Select(x => x.Information.Name).ToArray());

		// It should subscribe
		var subscribeMsg = await _subscribed.Task.WaitAsync(Timeout);
		Assert.Equal(SystemStreams.AuthorizationPolicyRegistryStream, subscribeMsg.EventStreamId);
	}

	[Fact]
	public void when_starting_and_read_backwards_has_not_completed_yet() {
		var sut = CreateSut([], new AuthorizationPolicySettings(AclsPolicy), _ => { });

		// It uses the fallback policy for stream access, and the legacy policy for endpoint access
		Assert.Equal(2, sut.EffectivePolicies.Length);
		Assert.Equal(FallbackPolicy, sut.EffectivePolicies[0].Information.Name);
		Assert.Equal(AclsPolicy, sut.EffectivePolicies[1].Information.Name);
	}

	[Theory]
	[InlineData(ClientMessage.NotHandled.Types.NotHandledReason.NotReady)]
	[InlineData(ClientMessage.NotHandled.Types.NotHandledReason.TooBusy)]
	public async Task when_starting_and_the_read_backwards_is_not_handled(
		ClientMessage.NotHandled.Types.NotHandledReason notHandledReason) {
		var longerTimeout = TimeSpan.FromSeconds(20); // Retries have a delay
		var failed = false;

		var sut = CreateSut(
			[new FakePolicySelectorPlugin("plugin", true)], new AuthorizationPolicySettings(AclsPolicy),
			m => {
				if (!failed) {
					failed = true;
					m.Envelope.ReplyWith(new ClientMessage.NotHandled(m.CorrelationId, notHandledReason, ""));
				} else
					m.Envelope.ReplyWith(CreateReadCompleted(m, [
						CreateResolvedEvent(new AuthorizationPolicySettings("plugin"), 0)
					]));
			});

		await sut.Start();

		// It retries and eventually updates the policy
		Assert.True(await _policyUpdated.WaitAsync(longerTimeout));
		Assert.Equal("plugin", sut.EffectivePolicies[0].Information.Name);
		Assert.Equal(AclsPolicy, sut.EffectivePolicies[1].Information.Name);

		// It should subscribe
		var subscribeMsg = await _subscribed.Task.WaitAsync(longerTimeout);
		Assert.Equal(SystemStreams.AuthorizationPolicyRegistryStream, subscribeMsg.EventStreamId);
	}

	[Theory]
	[InlineData(ReadStreamResult.AccessDenied, true, AclsPolicy)]
	[InlineData(ReadStreamResult.Error, true, AclsPolicy)]
	[InlineData(ReadStreamResult.StreamDeleted, false, AclsPolicy)]
	[InlineData(ReadStreamResult.NoStream, false, AclsPolicy)]
	[InlineData(ReadStreamResult.StreamDeleted, false, "plugin")]
	[InlineData(ReadStreamResult.NoStream, false, "plugin")]
	public async Task when_starting_and_the_read_backwards_fails_and_cannot_be_retried(
		ReadStreamResult readResult, bool fatal, string defaultPolicy) {
		var longerTimeout = TimeSpan.FromSeconds(20);
		Exception? actualException = null;

		var sut = CreateSut(
			[new FakePolicySelectorPlugin("plugin", true)], new AuthorizationPolicySettings(defaultPolicy),
			m => {
				m.Envelope.ReplyWith(ErrorResponse(m, readResult));
			});

		try {
			await sut.Start();
		} catch (Exception ex) {
			actualException = ex;
		}

		if (fatal) {
			// It throws a fatal exception
			Assert.NotNull(actualException);
			Assert.IsType<ApplicationInitializationException>(actualException);

			// It uses the fallback policy
			Assert.Equal(new[]{FallbackPolicy, AclsPolicy},
				sut.EffectivePolicies.Select(x => x.Information.Name).ToArray());
		} else {
			// It does not throw an exception
			Assert.Null(actualException);

			// It uses the default policy
			Assert.True(await _policyUpdated.WaitAsync(Timeout));
			Assert.Equal(defaultPolicy == AclsPolicy ? [AclsPolicy] : [defaultPolicy, AclsPolicy],
				sut.EffectivePolicies.Select(x => x.Information.Name).ToArray());

			// It subscribes
			var subscribeMsg = await _subscribed.Task.WaitAsync(longerTimeout);
			Assert.Equal(SystemStreams.AuthorizationPolicyRegistryStream, subscribeMsg.EventStreamId);
		}
	}

	public static TheoryData<SettingsEventTestCase> SettingsTestCases => new() {
		new SettingsEventTestCase("disabled", shouldCallEnable: true, shouldCallDisable: false, [], new FakePolicySelectorPlugin("disabled", false)),
		new SettingsEventTestCase("valid", shouldCallEnable: true, shouldCallDisable: true, ["valid", AclsPolicy], new FakePolicySelectorPlugin("valid", true)),
		new SettingsEventTestCase(AclsPolicy, shouldCallEnable: false, shouldCallDisable: false, [AclsPolicy], null),
		new SettingsEventTestCase(FallbackPolicy, shouldCallEnable: false, shouldCallDisable: false, [FallbackPolicy, AclsPolicy], null),
		new SettingsEventTestCase("not found", shouldCallEnable: false, shouldCallDisable: false, [], new FakePolicySelectorPlugin("custom", true)),
		new SettingsEventTestCase("invalid", "invalid", "invalid", shouldCallEnable: false, shouldCallDisable: false, [], new FakePolicySelectorPlugin("invalid", true)),
		new SettingsEventTestCase("invalid json", SystemEventTypes.AuthorizationPolicyChanged, "invalid", shouldCallEnable: false, shouldCallDisable: false, [], new FakePolicySelectorPlugin("invalid json", true)),
		new SettingsEventTestCase("invalid event type", "invalid", JsonSerializer.Serialize(new AuthorizationPolicySettings(AclsPolicy)), shouldCallEnable: false, shouldCallDisable: false, [], new FakePolicySelectorPlugin("invalid event type", true)),
		new SettingsEventTestCase("empty json object", SystemEventTypes.AuthorizationPolicyChanged, "{}", shouldCallEnable: false, shouldCallDisable: false, [FallbackPolicy, AclsPolicy], new FakePolicySelectorPlugin("empty json object", true)),
		new SettingsEventTestCase("valid json without policyType", SystemEventTypes.AuthorizationPolicyChanged, "{}", shouldCallEnable: false, shouldCallDisable: false, [FallbackPolicy, AclsPolicy], new FakePolicySelectorPlugin("valid json without policyType", true)),
		new SettingsEventTestCase("empty policyType", SystemEventTypes.AuthorizationPolicyChanged, "{}", shouldCallEnable: false, shouldCallDisable: false, [FallbackPolicy, AclsPolicy], new FakePolicySelectorPlugin("valid json without policyType", true)),
	};

	[Theory]
	[MemberData(nameof(SettingsTestCases))]
	public async Task when_settings_events_exist_in_the_stream(SettingsEventTestCase testCase) {
		var finishedEvent = CreateResolvedEvent(new AuthorizationPolicySettings("finished"), 3);

		// Set up expected policies
		var appliesPolicies = testCase.AppliesPolicies.Length > 0
			? testCase.AppliesPolicies
			: [FallbackPolicy, AclsPolicy];
		var expectedPolicies = new[] {
			appliesPolicies,
			["finished", AclsPolicy]
		};

		// Set up available plugins
		var availablePlugins = new Dictionary<string, FakePolicySelectorPlugin> {
			{ "finished", new FakePolicySelectorPlugin("finished", true) },
		};
		if (testCase.CustomPlugin is not null) {
			availablePlugins.Add(testCase.CustomPlugin.CommandLineName, testCase.CustomPlugin);
		}

		var sut = CreateSut(
			availablePlugins.Values.ToArray(), new AuthorizationPolicySettings(AclsPolicy),
			m => {
				m.Envelope.ReplyWith(CreateReadCompleted(m, [testCase.Event]));
			});
		await sut.Start();

		// Wait for the policy to be updated
		Assert.True(await _policyUpdated.WaitAsync(Timeout));
		_policyUpdated.Reset();

		if (testCase.ShouldCallEnable) {
			// It attempted to enable the policy
			Assert.True(await availablePlugins[testCase.PolicyName].OnEnabled.WaitAsync(Timeout));
		}

		if (testCase.AppliesPolicies.Length > 0) {
			// The correct policies are in effect
			for (var i = 0; i < testCase.AppliesPolicies.Length; i++) {
				Assert.Equal(testCase.AppliesPolicies[i], sut.EffectivePolicies[i].Information.Name);
			}
		} else {
			// All events are invalid or cannot be enabled
			// The fallback policy should be used
			Assert.Equal(FallbackPolicy, sut.EffectivePolicies[0].Information.Name);
			Assert.Equal(AclsPolicy, sut.EffectivePolicies[1].Information.Name);
		}

		// It should subscribe
		var subscribeMsg = await _subscribed.Task.WaitAsync(Timeout);

		// Send the finished event
		subscribeMsg.Envelope.ReplyWith(
			new ClientMessage.StreamEventAppeared(subscribeMsg.CorrelationId, finishedEvent));

		// The finished policy is applied
		Assert.True(await _policyUpdated.WaitAsync(Timeout));
		Assert.Equal("finished", sut.EffectivePolicies[0].Information.Name);

		if (testCase.ShouldCallDisable) {
			// The previous policy is disabled
			Assert.True(await availablePlugins[testCase.PolicyName].OnDisabled.WaitAsync(Timeout));
		}

		// The policy updates contain the expected policies
		for (var i = 0; i < expectedPolicies.Length; i++) {
			Assert.Equal(expectedPolicies[i], _policyUpdates[i].Select(x => x.Information.Name));
		}
	}

	[Theory]
	[MemberData(nameof(SettingsTestCases))]
	public async Task when_settings_events_are_received_by_the_subscription(SettingsEventTestCase testCase) {
		var finishedEvent = CreateResolvedEvent(new AuthorizationPolicySettings("finished"), 3);

		// Set up expected policies
		var expectedPolicies = new List<string[]>();
		expectedPolicies.Add(["start", AclsPolicy]);
		if (testCase.AppliesPolicies.Length > 0) {
			expectedPolicies.Add(testCase.AppliesPolicies);
		}
		expectedPolicies.Add(["finished", AclsPolicy]);

		// Set up available plugins
		var availablePlugins = new Dictionary<string, FakePolicySelectorPlugin> {
			{ "start", new FakePolicySelectorPlugin("start", true) },
			{ "finished", new FakePolicySelectorPlugin("finished", true) },
		};
		if (testCase.CustomPlugin is not null) {
			availablePlugins.Add(testCase.CustomPlugin.CommandLineName, testCase.CustomPlugin);
		}

		var sut = CreateSut(
			availablePlugins.Values.ToArray(), new AuthorizationPolicySettings(AclsPolicy),
			m => {
				m.Envelope.ReplyWith(CreateReadCompleted(m,
					[CreateResolvedEvent(new AuthorizationPolicySettings("start"), 0)]));
			});
		await sut.Start();

		// Load the start policy
		Assert.True(await _policyUpdated.WaitAsync(Timeout));
		_policyUpdated.Reset();
		Assert.Equal("start", sut.EffectivePolicies[0].Information.Name);

		// Wait for the subscription
		var subscribeMsg = await _subscribed.Task.WaitAsync(Timeout);

		// Send the new settings
		subscribeMsg.Envelope.ReplyWith(
			new ClientMessage.StreamEventAppeared(subscribeMsg.CorrelationId, testCase.Event));

		if (testCase.ShouldCallEnable) {
			// Attempt to enable the new policy
			Assert.True(await availablePlugins[testCase.PolicyName].OnEnabled.WaitAsync(Timeout));
		}

		if (testCase.AppliesPolicies.Length > 0) {
			// The policy was updated
			Assert.True(await _policyUpdated.WaitAsync(Timeout));
			_policyUpdated.Reset();

			// The start policy was disabled
			Assert.True(await availablePlugins["start"].OnDisabled.WaitAsync(Timeout));

			// The correct policies are in effect
			Assert.Equal(testCase.AppliesPolicies, sut.EffectivePolicies.Select(x => x.Information.Name).ToArray());
		}

		// Send the finished settings event
		subscribeMsg.Envelope.ReplyWith(
			new ClientMessage.StreamEventAppeared(subscribeMsg.CorrelationId, finishedEvent));

		// The finished policy was applied
		Assert.True(await _policyUpdated.WaitAsync(Timeout));
		_policyUpdated.Reset();
		Assert.Equal("finished", sut.EffectivePolicies[0].Information.Name);

		if (testCase.ShouldCallDisable) {
			// The policy was disabled
			Assert.True(await availablePlugins[testCase.PolicyName].OnDisabled.WaitAsync(Timeout));
		}

		// The policy updates contain the expected policies
		for (var i = 0; i < expectedPolicies.Count; i++) {
			Assert.Equal(expectedPolicies[i], _policyUpdates[i].Select(x => x.Information.Name));
		}
	}

	private static ClientMessage.ReadStreamEventsBackwardCompleted
		ErrorResponse(ClientMessage.ReadStreamEventsBackward msg, ReadStreamResult result) =>
		new(
			msg.CorrelationId, msg.EventStreamId, msg.FromEventNumber, msg.MaxCount,
			result, [], StreamMetadata.Empty, true, "",
			msg.FromEventNumber, msg.FromEventNumber, true, 0L);

	private static ResolvedEvent CreateResolvedEvent(AuthorizationPolicySettings settings, long eventNumber) {
		var data = JsonSerializer.SerializeToUtf8Bytes(settings, SerializerOptions);
		return CreateResolvedEvent(SystemEventTypes.AuthorizationPolicyChanged, data, eventNumber);
	}

	private static ResolvedEvent CreateResolvedEvent(string eventType, byte[] data, long eventNumber) {
		var logPosition = eventNumber * 100;
		var prepare = new PrepareLogRecord(logPosition, Guid.NewGuid(), Guid.NewGuid(),
			transactionPosition: logPosition,
			transactionOffset: 0,
			SystemStreams.AuthorizationPolicyRegistryStream,
			eventStreamIdSize: null,
			expectedVersion: eventNumber, DateTime.Now, PrepareFlags.Data,
			eventType, eventTypeSize: null, data, ReadOnlyMemory<byte>.Empty);
		var eventRecord = new EventRecord(eventNumber, prepare, SystemStreams.AuthorizationPolicyRegistryStream, eventType);
		return ResolvedEvent.ForUnresolvedEvent(eventRecord, logPosition);
	}

	private ClientMessage.ReadStreamEventsBackwardCompleted CreateReadCompleted(
		ClientMessage.ReadStreamEventsBackward msg, ResolvedEvent[] events) =>
		new(msg.CorrelationId, msg.EventStreamId, msg.FromEventNumber, msg.MaxCount,
			ReadStreamResult.Success, events, StreamMetadata.Empty, true, "",
			msg.FromEventNumber, msg.FromEventNumber, true, 1000L);

	public readonly struct DefaultSettingsTestCase(
		string policyName,
		AuthorizationPolicySettings defaultSettings,
		string[] appliesPolicies,
		FakePolicySelectorPlugin? customPlugin) {
		public readonly string PolicyName = policyName;
		public readonly AuthorizationPolicySettings DefaultSettings = defaultSettings;
		public readonly string[] AppliesPolicies = appliesPolicies;
		public readonly FakePolicySelectorPlugin? CustomPlugin = customPlugin;

		public override string ToString() {
			return $"default: '{PolicyName}' applies [{string.Join(", ", AppliesPolicies)}]";
		}
	}

	public readonly struct SettingsEventTestCase {
		public readonly ResolvedEvent Event;
		public readonly string PolicyName;
		public readonly bool ShouldCallEnable;
		public readonly bool ShouldCallDisable;
		public readonly string[] AppliesPolicies;
		public readonly FakePolicySelectorPlugin? CustomPlugin;

		public SettingsEventTestCase(string policyName, bool shouldCallEnable, bool shouldCallDisable, string[] appliesPolicies, FakePolicySelectorPlugin? customPlugin) {
			PolicyName = policyName;
			// EventNumber hardcoded to 2. Allows for a default and start event
			Event = CreateResolvedEvent(new AuthorizationPolicySettings(policyName), 2);
			ShouldCallEnable = shouldCallEnable;
			ShouldCallDisable = shouldCallDisable;
			AppliesPolicies = appliesPolicies;
			CustomPlugin = customPlugin;
		}

		public SettingsEventTestCase(string policyName, string eventType, string data, bool shouldCallEnable, bool shouldCallDisable, string[] appliesPolicies, FakePolicySelectorPlugin? customPlugin) {
			PolicyName = policyName;
			// EventNumber hardcoded to 2. Allows for a default and start event
			Event = CreateResolvedEvent(eventType, Encoding.UTF8.GetBytes(data), 2);
			ShouldCallEnable = shouldCallEnable;
			ShouldCallDisable = shouldCallDisable;
			AppliesPolicies = appliesPolicies;
			CustomPlugin = customPlugin;
		}

		public override string ToString() {
			return AppliesPolicies.Length > 0
				? $"'{PolicyName}' policy applies [{string.Join(", ", AppliesPolicies)}]"
				: $"'{PolicyName}' policy does not update the policies";
		}
	}

	private class AuthRegistryTestPublisher : IPublisher {
		private readonly Action<ClientMessage.ReadStreamEventsBackward> _onReadBackward;
		private readonly Action<ClientMessage.SubscribeToStream> _onSubscribed;
		public readonly TaskCompletionSource<ClientMessage.SubscribeToStream> Subscribed = new();

		public AuthRegistryTestPublisher(Action<ClientMessage.ReadStreamEventsBackward> onReadBackward) {
			_onReadBackward = onReadBackward;
			_onSubscribed = m => {
				m.Envelope.ReplyWith(new ClientMessage.SubscriptionConfirmation(m.CorrelationId, 100, 0));
				Subscribed.TrySetResult(m);
			} ;
		}
		public void Publish(Message message) {
			switch (message)
			{
				case ClientMessage.ReadStreamEventsBackward read:
					_onReadBackward(read);
					break;
				case ClientMessage.SubscribeToStream subscribe:
					_onSubscribed(subscribe);
					break;
			}
		}
	}
}
