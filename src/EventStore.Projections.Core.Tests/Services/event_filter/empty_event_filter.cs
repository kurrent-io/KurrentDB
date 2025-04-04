// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.event_filter;

[TestFixture]
public class empty_event_filter : TestFixtureWithEventFilter {
	[Test]
	public void cannot_be_built() {
		Assert.IsAssignableFrom(typeof(InvalidOperationException), _exception);
	}
}
