// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json;
using EventStore.Auth.StreamPolicyPlugin.Schema;
using EventStore.Core.Authorization;
using EventStore.Core.Bus;
using EventStore.Core.Services;
using EventStore.Core.Services.UserManagement;
using EventStore.Plugins.Authorization;
using Serilog;
using Policy = EventStore.Core.Authorization.Policy;

namespace EventStore.Auth.StreamPolicyPlugin;

public sealed class StreamPolicySelector : StreamBasedPolicySelector {
	public const string PolicyName = "custom-stream-policy";
	public const string PolicyStream = "$policies";
	public const string PolicyEventType = "$policy-updated";
	private static readonly ILogger Logger = Log.ForContext<StreamPolicySelector>();

	public StreamPolicySelector(IPublisher publisher, CancellationToken ct) : base(
		publisher: publisher,
		stream: PolicyStream,
		user: SystemAccounts.System,
		tryParsePolicy: TryParsePolicy,
		notReadyPolicy: BuildNotReadyPolicy(),
		ct: ct) {
	}

	private static ReadOnlyPolicy BuildNotReadyPolicy() {
		// we build a policy that denies all stream access as we don't want any stream operation to seep through while
		// we're setting things up. note that system or admin users will still be able to get through as there are some
		// preliminary checks prior to applying the rules.
		var streamAssertion = new StreamPolicyAssertion(_ => AccessPolicy.None);
		var policy = new Policy($"{PolicyName}-notready", 1, DateTimeOffset.MinValue);
		policy.Add(Operations.Streams.Read, streamAssertion);
		policy.Add(Operations.Streams.Write, streamAssertion);
		policy.Add(Operations.Streams.Delete, streamAssertion);
		policy.Add(Operations.Streams.MetadataRead, streamAssertion);
		policy.Add(Operations.Streams.MetadataWrite, streamAssertion);
		// Persistent Subscriptions
		var subscriptionAccess = new RequireStreamReadAssertion(streamAssertion);
		policy.Add(Operations.Subscriptions.ProcessMessages, subscriptionAccess);
		return policy.AsReadOnly();
	}

	// The default policy allows:
	// - User access to public streams
	// - Admin only access to system streams
	// - User read-only access to standard projection streams
	public static Schema.Policy DefaultPolicy =
		new() {
			StreamPolicies = new Dictionary<string, Schema.AccessPolicy> {
				{
					"publicDefault", new Schema.AccessPolicy {
						Readers = [SystemRoles.All],
						Writers = [SystemRoles.All],
						Deleters = [SystemRoles.All],
						MetadataReaders = [SystemRoles.All],
						MetadataWriters = [SystemRoles.All],
					}
				}, {
					"adminsDefault", new Schema.AccessPolicy {
						Readers = [SystemRoles.Admins],
						Writers = [SystemRoles.Admins],
						Deleters = [SystemRoles.Admins],
						MetadataReaders = [SystemRoles.Admins],
						MetadataWriters = [SystemRoles.Admins],
					}
				}, {
					"projectionsDefault", new Schema.AccessPolicy {
						Readers = [SystemRoles.All],
						Writers = [SystemRoles.Admins],
						Deleters = [SystemRoles.Admins],
						MetadataReaders = [SystemRoles.All],
						MetadataWriters = [SystemRoles.Admins],
					}
				}
			},
			DefaultStreamRules = new DefaultStreamRules {
				SystemStreams = "adminsDefault",
				UserStreams = "publicDefault"
			},
			StreamRules = [
				new StreamRule {
					StartsWith = "$et-",
					Policy = "projectionsDefault",
				},
				new StreamRule {
					StartsWith = "$ce-",
					Policy = "projectionsDefault",
				},
				new StreamRule {
					StartsWith = "$bc-",
					Policy = "projectionsDefault",
				},
				new StreamRule {
					StartsWith = "$category-",
					Policy = "projectionsDefault",
				},
				new StreamRule {
					StartsWith = "$streams",
					Policy = "projectionsDefault",
				}
			],
		};

	private static readonly JsonSerializerOptions SerializeOptions = new() {
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	};

	private static bool TryParsePolicy(string eventType, ReadOnlySpan<byte> eventData, out ReadOnlyPolicy readOnlyPolicy) {
		if (eventType != PolicyEventType) {
			Logger.Error("Expected event type: {expectedEventType} but was {actualEventType}", PolicyEventType, eventType);
			readOnlyPolicy = default!;
			return false;
		}

		if (!TryParseStreamRules(eventData, out var streamRules)) {
			readOnlyPolicy = default!;
			return false;
		}

		var streamAssertion = new StreamPolicyAssertion(streamRules);
		var policy = new Policy(PolicyName, 1, DateTimeOffset.MinValue);
		// Streams
		policy.Add(Operations.Streams.Read, streamAssertion);
		policy.Add(Operations.Streams.Write, streamAssertion);
		policy.Add(Operations.Streams.Delete, streamAssertion);
		policy.Add(Operations.Streams.MetadataRead, streamAssertion);
		policy.Add(Operations.Streams.MetadataWrite, streamAssertion);
		// Persistent Subscriptions
		var subscriptionAccess = new RequireStreamReadAssertion(streamAssertion);
		policy.Add(Operations.Subscriptions.ProcessMessages, subscriptionAccess);
		readOnlyPolicy = policy.AsReadOnly();
		return true;
	}

	public static byte[] SerializePolicy(Schema.Policy policy) => JsonSerializer.SerializeToUtf8Bytes(policy, SerializeOptions);

	private static bool TryParseStreamRules(ReadOnlySpan<byte> data, out Func<string, AccessPolicy> streamRules) {
		streamRules = default!;

		try {
			var policy = JsonSerializer.Deserialize<Schema.Policy>(data, SerializeOptions)!;

			var streamPolicies = new Dictionary<string, AccessPolicy>();
			foreach (var (name, accessPolicy) in policy.StreamPolicies) {
				streamPolicies[name] = new AccessPolicy(
					readers: accessPolicy.Readers,
					writers: accessPolicy.Writers,
					deleters: accessPolicy.Deleters,
					metadataReaders: accessPolicy.MetadataReaders,
					metadataWriters: accessPolicy.MetadataWriters);
			}

			var streamPrefixRules = new List<StreamPrefixRule>();
			foreach (var streamRule in policy.StreamRules) {
				if (string.IsNullOrEmpty(streamRule.StartsWith)) {
					Logger.Error("Stream rule at index: {streamRuleIndex} and referring to policy: {policy} has an empty prefix",
						Array.IndexOf(policy.StreamRules, streamRule), streamRule.Policy);
					return false;
				}
				if (!streamPolicies.TryGetValue(streamRule.Policy, out var accessPolicy)) {
					Logger.Error("Stream rule for prefix: {streamPrefix} refers to an undefined policy: {policy}", streamRule.StartsWith, streamRule.Policy);
					return false;
				}
				streamPrefixRules.Add(new StreamPrefixRule(streamRule.StartsWith, accessPolicy));
			}

			if (!streamPolicies.TryGetValue(policy.DefaultStreamRules.SystemStreams, out var systemStreamsPolicy)) {
				Logger.Error("Default {streamType} stream rule refers to an undefined policy: {policy}", "system", policy.DefaultStreamRules.SystemStreams);
				return false;
			}

			if (!streamPolicies.TryGetValue(policy.DefaultStreamRules.UserStreams, out var userStreamsPolicy)) {
				Logger.Error("Default {streamType} stream rule refers to an undefined policy: {policy}", "user", policy.DefaultStreamRules.UserStreams);
				return false;
			}

			var rules = streamPrefixRules.ToArray();
			streamRules = streamId => {
				foreach (var rule in rules)
					if (rule.TryHandle(streamId, out var permissions))
						return permissions;
				return SystemStreams.IsSystemStream(streamId) ? systemStreamsPolicy : userStreamsPolicy;
			};
			return true;
		} catch (JsonException exc) {
			Logger.Error(exc, "Error while deserializing new policy");
			return false;
		} catch (ArgumentNullException exc) {
			Logger.Error(exc, "Error while deserializing new policy");
			return false;
		}
	}
}
