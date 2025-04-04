// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Projections.Core.Services.Processing;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.projection_version;

[TestFixture]
public class when_comparing {
	[Test]
	public void equal() {
		var v1 = new ProjectionVersion(10, 5, 6);
		var v2 = new ProjectionVersion(10, 5, 6);

		Assert.AreEqual(v1, v2);
	}

	[Test]
	public void not_equal_id() {
		var v1 = new ProjectionVersion(10, 5, 6);
		var v2 = new ProjectionVersion(11, 5, 6);

		Assert.AreNotEqual(v1, v2);
	}

	[Test]
	public void not_equal_epoch() {
		var v1 = new ProjectionVersion(10, 5, 6);
		var v2 = new ProjectionVersion(11, 6, 6);

		Assert.AreNotEqual(v1, v2);
	}

	[Test]
	public void not_equal_version() {
		var v1 = new ProjectionVersion(10, 5, 6);
		var v2 = new ProjectionVersion(10, 5, 7);

		Assert.AreNotEqual(v1, v2);
	}
}
