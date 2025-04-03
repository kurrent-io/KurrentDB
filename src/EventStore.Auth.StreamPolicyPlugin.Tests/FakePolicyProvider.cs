// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.Services;

namespace EventStore.Auth.StreamPolicyPlugin.Tests;

public class FakePolicyProvider {
	private readonly Dictionary<string, AccessPolicy> _streamRules = new();
	private readonly AccessPolicy _publicStreamRule;

	public FakePolicyProvider(string restrictedStreamName, string restrictedStreamUser) {
		_streamRules.Add(restrictedStreamName, new AccessPolicy(
			[restrictedStreamUser],
			[restrictedStreamUser],
			[restrictedStreamUser],
			[restrictedStreamUser],
			[restrictedStreamUser]
		));
		_publicStreamRule = new(
			[SystemRoles.All],
			[SystemRoles.All],
			[SystemRoles.All],
			[SystemRoles.All],
			[SystemRoles.All]
		);
	}

	public void SetCustomAccessPolicy(string streamId, AccessPolicy accessPolicy) {
		_streamRules.Add(streamId, accessPolicy);
	}

	public AccessPolicy GetAccessPolicyFor(string streamId) {
		return _streamRules.TryGetValue(streamId, out var rule) ? rule : _publicStreamRule;
	}
}
