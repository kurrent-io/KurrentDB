// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Core.Authorization;

public struct AssertionInformation {
	public Grant Grant { get; }
	private readonly string _assertion;

	public AssertionInformation(string type, string assertion, Grant grant) {
		Grant = grant;
		_assertion = $"{type}:{assertion}:{grant}";
	}

	public override string ToString() {
		return _assertion;
	}
}
