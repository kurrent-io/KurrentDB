// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json.Serialization;

namespace EventStore.Auth.StreamPolicyPlugin.Schema;

public class Policy : IJsonOnDeserialized {
	[JsonRequired]
	public Dictionary<string, AccessPolicy> StreamPolicies { get; init; } = default!;

	[JsonRequired]
	public StreamRule[] StreamRules { get; init; } = default!;

	[JsonRequired]
	public DefaultStreamRules DefaultStreamRules { get; init; } = default!;

	public void OnDeserialized() {
		if (StreamPolicies is null)
			throw new ArgumentNullException($"{nameof(StreamPolicies)} cannot be null");
		if (StreamRules is null)
			throw new ArgumentNullException($"{nameof(StreamRules)} cannot be null");
		if (DefaultStreamRules is null)
			throw new ArgumentNullException($"{nameof(DefaultStreamRules)} cannot be null");
	}
}
