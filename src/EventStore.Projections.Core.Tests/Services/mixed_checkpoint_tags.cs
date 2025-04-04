// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services;

[TestFixture]
public class mixed_checkpoint_tags {
	private readonly CheckpointTag _a = CheckpointTag.FromStreamPosition(0, "stream1", 9);
	private readonly CheckpointTag _b = CheckpointTag.FromStreamPosition(0, "stream2", 15);
	private readonly CheckpointTag _c = CheckpointTag.FromPosition(0, 50, 29);

	[Test]
	public void are_not_equal() {
		Assert.AreNotEqual(_a, _b);
		Assert.AreNotEqual(_a, _c);
		Assert.AreNotEqual(_b, _c);

		Assert.IsTrue(_a != _b);
		Assert.IsTrue(_a != _c);
		Assert.IsTrue(_b != _c);
	}

	[Test]
	public void cannot_be_compared() {
		Assert.IsTrue(throws(() => _a > _b));
		Assert.IsTrue(throws(() => _a >= _b));
		Assert.IsTrue(throws(() => _a > _c));
		Assert.IsTrue(throws(() => _a >= _c));
		Assert.IsTrue(throws(() => _b > _c));
		Assert.IsTrue(throws(() => _b >= _c));
		Assert.IsTrue(throws(() => _a < _b));
		Assert.IsTrue(throws(() => _a <= _b));
		Assert.IsTrue(throws(() => _a < _c));
		Assert.IsTrue(throws(() => _a <= _c));
		Assert.IsTrue(throws(() => _b < _c));
		Assert.IsTrue(throws(() => _b <= _c));
	}

	private bool throws(Func<bool> func) {
		try {
			func();
			return false;
		} catch (Exception) {
			return true;
		}
	}
}
