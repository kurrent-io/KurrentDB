// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Security.Claims;

namespace EventStore.Auth.LegacyAuthorizationWithStreamAuthorizationDisabled;

public readonly struct AssertionMatch {
	public readonly PolicyInformation Policy;
	public readonly AssertionInformation Assertion;
	public readonly IReadOnlyList<Claim> Matches;

	public AssertionMatch(PolicyInformation policy, AssertionInformation assertion, params Claim[] matches) : this(
		policy, assertion, (IReadOnlyList<Claim>)matches) {
	}

	public AssertionMatch(PolicyInformation policy, AssertionInformation assertion, IReadOnlyList<Claim> matches) {
		Policy = policy;
		Assertion = assertion;
		Matches = matches;
	}

	public override string ToString() {
		return $"{Policy} : {Assertion}, ${string.Join(Environment.NewLine + "\t", Matches)}";
	}
}
