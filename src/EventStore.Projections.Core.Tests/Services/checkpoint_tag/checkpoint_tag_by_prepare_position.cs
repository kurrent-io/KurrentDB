// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#pragma warning disable 1718

using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.checkpoint_tag;

[TestFixture]
public class checkpoint_tag_by_prepare_position {
	private readonly CheckpointTag _aa = CheckpointTag.FromPreparePosition(1, 9);
	private readonly CheckpointTag _b1 = CheckpointTag.FromPreparePosition(1, 15);
	private readonly CheckpointTag _b2 = CheckpointTag.FromPreparePosition(1, 15);
	private readonly CheckpointTag _cc = CheckpointTag.FromPreparePosition(1, 29);
	private readonly CheckpointTag _d1 = CheckpointTag.FromPreparePosition(1, 35);
	private readonly CheckpointTag _d2 = CheckpointTag.FromPreparePosition(1, 35);

	[Test]
	public void equal_equals() {
		Assert.IsTrue(_aa.Equals(_aa));
	}

	[Test]
	public void equal_operator() {
		Assert.IsTrue(_b1 == _b1);
		Assert.IsTrue(_b1 == _b2);
	}

	[Test]
	public void less_operator() {
		Assert.IsTrue(_aa < _b1);
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
		Assert.IsFalse(_d2 > _d1);
		Assert.IsFalse(_d2 > _d2);
	}

	[Test]
	public void greater_or_equal_operator() {
		Assert.IsTrue(_d1 >= _cc);
		Assert.IsTrue(_d2 >= _d1);
		Assert.IsTrue(_b2 >= _b2);
	}
}
