// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

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
		var cp = new ClaimsPrincipal(new ClaimsIdentity([new("test1", "value1")]));
		var context = new EvaluationContext(new Operation("test", "test"), CancellationToken.None);
		Assert.False(await assertion.Evaluate(cp, new("test", "test"), new("test", 1, DateTimeOffset.MaxValue), context));
		Assert.AreEqual(Grant.Unknown, context.Grant);
	}

	[Test]
	public async Task NonMatchingHasNoMatches() {
		var assertion = new MultipleClaimMatchAssertion(Grant.Deny, MultipleMatchMode.None,
			new Claim("test1", "value1"), new Claim("test2", "value2"));
		var cp = new ClaimsPrincipal(new ClaimsIdentity([new("test3", "value3")]));
		var context = new EvaluationContext(new("test", "test"), CancellationToken.None);
		Assert.True(await assertion.Evaluate(cp, new("test", "test"), new("test", 1, DateTimeOffset.MaxValue), context));
		Assert.AreEqual(Grant.Deny, context.Grant);
	}
}
