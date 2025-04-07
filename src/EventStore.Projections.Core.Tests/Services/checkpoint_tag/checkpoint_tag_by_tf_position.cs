// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using EventStore.Projections.Core.Services.Processing.Checkpointing;
using NUnit.Framework;

#pragma warning disable 1718 // allow a == a comparison

namespace EventStore.Projections.Core.Tests.Services.checkpoint_tag;

[TestFixture]
public class checkpoint_tag_by_tf_position {
	private readonly CheckpointTag _aa = CheckpointTag.FromPosition(1, 10, 9);
	private readonly CheckpointTag _b1 = CheckpointTag.FromPosition(1, 20, 15);
	private readonly CheckpointTag _b2 = CheckpointTag.FromPosition(1, 20, 17);
	private readonly CheckpointTag _cc = CheckpointTag.FromPosition(1, 30, 29);
	private readonly CheckpointTag _d1 = CheckpointTag.FromPosition(1, 40, 35);
	private readonly CheckpointTag _d2 = CheckpointTag.FromPosition(1, 40, 36);

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

