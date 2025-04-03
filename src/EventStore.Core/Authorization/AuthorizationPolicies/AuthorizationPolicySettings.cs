// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Core.Authorization.AuthorizationPolicies;

public class AuthorizationPolicySettings {
	public string StreamAccessPolicyType { get; set; }

	public AuthorizationPolicySettings() {
		StreamAccessPolicyType = FallbackStreamAccessPolicySelector.FallbackPolicyName;
	}

	public AuthorizationPolicySettings(string streamAccessPolicyType) {
		StreamAccessPolicyType = streamAccessPolicyType;
	}
	public override string ToString() => $"{nameof(StreamAccessPolicyType)}: '{StreamAccessPolicyType}'";
}
