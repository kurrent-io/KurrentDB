// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Auth.StreamPolicyPlugin;
public class StreamPrefixRule {
	private readonly string _startsWith;
	private readonly AccessPolicy _streamAccessPolicy;

	public StreamPrefixRule(string startsWith, AccessPolicy streamAccessPolicy) {
		if (string.IsNullOrEmpty(startsWith))
			throw new ArgumentNullException(nameof(startsWith));
		_startsWith = startsWith;
		_streamAccessPolicy = streamAccessPolicy;
	}

	public bool TryHandle(string streamId, out AccessPolicy policy) {
		if (!streamId.StartsWith(_startsWith)) {
			policy = default!;
			return false;
		}

		policy = _streamAccessPolicy;
		return true;
	}
}
