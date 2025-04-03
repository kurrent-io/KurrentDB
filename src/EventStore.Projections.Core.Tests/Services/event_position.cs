// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.Data;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services;

#pragma warning disable 1718 // allow a == a comparison
[TestFixture]
public class event_position {
	private readonly TFPos _aa = new TFPos(10, 9);
	private readonly TFPos _b1 = new TFPos(20, 15);
	private readonly TFPos _b2 = new TFPos(20, 17);
	private readonly TFPos _cc = new TFPos(30, 29);
	private readonly TFPos _d1 = new TFPos(40, 35);
	private readonly TFPos _d2 = new TFPos(40, 36);

	[Test]
	public void equal_equals() {
		Assert.IsTrue(_aa.Equals(_aa));
	}

	[Test]
	public void equal_operator() {
		Assert.IsTrue(_b1 == _b1);
	}

	[Test]
	public void less_operator() {
		Assert.IsTrue(_aa < _b1);
		Assert.IsTrue(_b1 < _b2);
	}

	[Test]
	public void less_or_equal_operator() {
		Assert.IsTrue(_aa <= _b1);
		Assert.IsTrue(_b1 <= _b2);
		Assert.IsTrue(_b2 <= _b2);
	}

	[Test]
	public void greater_operator() {
		Assert.IsTrue(_d1 > _cc);
		Assert.IsTrue(_d2 > _d1);
	}

	[Test]
	public void greater_or_equal_operator() {
		Assert.IsTrue(_d1 >= _cc);
		Assert.IsTrue(_d2 >= _d1);
		Assert.IsTrue(_b2 >= _b2);
	}
}
#pragma warning restore 1718

