// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using EventStore.Core.Data;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using NUnit.Framework;

#pragma warning disable 1718 // allow a == a comparison

namespace EventStore.Projections.Core.Tests.Services.checkpoint_tag;

[TestFixture]
public class checkpoint_tag_phase {
	private readonly CheckpointTag _p0 = CheckpointTag.FromPosition(0, 1000, 9);
	private readonly CheckpointTag _p1a = CheckpointTag.FromPosition(1, 500, 400);
	private readonly CheckpointTag _p1b = CheckpointTag.FromPosition(1, 500, 450);
	private readonly CheckpointTag _p2 = CheckpointTag.FromPosition(2, 30, 29);
	private readonly CheckpointTag _p3 = CheckpointTag.FromStreamPosition(3, "stream", 100);

	private readonly CheckpointTag _p4 = CheckpointTag.FromEventTypeIndexPositions(
		4, new TFPos(200, 150), new Dictionary<string, long> {{"a", 1}});

	[Test]
	public void equal_equals() {
		Assert.IsTrue(_p0.Equals(_p0));
		Assert.IsTrue(_p1a.Equals(_p1a));
		Assert.IsTrue(_p1b.Equals(_p1b));
		Assert.IsTrue(_p2.Equals(_p2));
		Assert.IsTrue(_p3.Equals(_p3));
	}

	[Test]
	public void equal_operator() {
		Assert.IsTrue(_p1a == _p1a);
	}

	[Test]
	public void less_operator() {
		Assert.IsTrue(_p0 < _p1a);
		Assert.IsTrue(_p2 < _p3);
		Assert.IsTrue(_p3 < _p4);
	}

	[Test]
	public void less_or_equal_operator() {
		Assert.IsTrue(_p1b <= _p2);
		Assert.IsTrue(_p2 <= _p4);
		Assert.IsTrue(_p3 <= _p3);
	}

	[Test]
	public void greater_operator() {
		Assert.IsTrue(_p4 > _p1a);
		Assert.IsTrue(_p1b > _p1a);
	}

	[Test]
	public void greater_or_equal_operator() {
		Assert.IsTrue(_p1a >= _p0);
		Assert.IsTrue(_p4 >= _p3);
		Assert.IsTrue(_p3 >= _p1a);
		Assert.IsTrue(_p2 >= _p2);
	}
}
#pragma warning restore 1718

