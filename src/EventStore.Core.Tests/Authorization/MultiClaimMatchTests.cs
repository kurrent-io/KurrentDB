// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Authorization;
using EventStore.Plugins.Authorization;
using NUnit.Framework;

namespace EventStore.Core.Tests.Authorization;

[TestFixture]
public class MultiClaimMatchTests {
	[Test]
	public async Task NoneMatchingHasMatches() {
		var assertion = new MultipleClaimMatchAssertion(Grant.Deny, MultipleMatchMode.None,
			new Claim("test1", "value1"), new Claim("test2", "value2"));
		var cp = new ClaimsPrincipal(new ClaimsIdentity(new Claim[] {new Claim("test1", "value1")}));
		var context = new EvaluationContext(new Operation("test", "test"), CancellationToken.None);
		Assert.False(await assertion.Evaluate(cp, new Operation("test", "test"),
			new PolicyInformation("test", 1, DateTimeOffset.MaxValue), context));
		Assert.AreEqual(Grant.Unknown, context.Grant);
	}

	[Test]
	public async Task NonMatchingHasNoMatches() {
		var assertion = new MultipleClaimMatchAssertion(Grant.Deny, MultipleMatchMode.None,
			new Claim("test1", "value1"), new Claim("test2", "value2"));
		var cp = new ClaimsPrincipal(new ClaimsIdentity(new Claim[] {new Claim("test3", "value3")}));
		var context = new EvaluationContext(new Operation("test", "test"), CancellationToken.None);
		Assert.True(await assertion.Evaluate(cp, new Operation("test", "test"),
			new PolicyInformation("test", 1, DateTimeOffset.MaxValue), context));
		Assert.AreEqual(Grant.Deny, context.Grant);
	}
}
