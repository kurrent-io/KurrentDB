// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using EventStore.Core.Tests.Helpers;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using NUnit.Framework;

#pragma warning disable 1718 // allow a == a comparison

namespace EventStore.Projections.Core.Tests.Services.checkpoint_tag;

[TestFixture]
public class checkpoint_tag_by_stream_positions {
	private readonly CheckpointTag _a1 = CheckpointTag.FromStreamPositions(
		1, new Dictionary<string, long> {{"a", 1}});

	private readonly CheckpointTag _b1 = CheckpointTag.FromStreamPositions(
		1, new Dictionary<string, long> {{"b", 1}});

	private readonly CheckpointTag _a1b1 = CheckpointTag.FromStreamPositions(
		1, new Dictionary<string, long> {{"a", 1}, {"b", 1}});

	private readonly CheckpointTag _a2b1 = CheckpointTag.FromStreamPositions(
		1, new Dictionary<string, long> {{"a", 2}, {"b", 1}});

	private readonly CheckpointTag _a1b2 = CheckpointTag.FromStreamPositions(
		1, new Dictionary<string, long> {{"a", 1}, {"b", 2}});

	private readonly CheckpointTag _a2b2 = CheckpointTag.FromStreamPositions(
		1, new Dictionary<string, long> {{"a", 2}, {"b", 2}});

	[Test]
	public void equal_equals() {
		Assert.IsTrue(_a1.Equals(_a1));
	}

	[Test]
	public void equal_operator() {
		Assert.IsTrue(_a2b1 == _a2b1);
	}

	[Test]
	public void less_operator() {
		Assert.IsTrue(_a1 < _a1b1);
		Assert.IsTrue(_a1 < _a1b2);
		Assert.IsTrue(_a1 < _a2b2);
		Assert.IsFalse(_a1b2 < _a1b2);
		Assert.IsFalse(_a1b2 < _a1b1);
		Assert.Throws<InvalidOperationException>(() => TestHelper.Consume(_a1b2 < _a2b1));
		Assert.Throws<InvalidOperationException>(() => TestHelper.Consume(_a1 < _b1));
	}

	[Test]
	public void less_or_equal_operator() {
		Assert.IsTrue(_a1 <= _a1b1);
		Assert.IsTrue(_a1 <= _a1b2);
		Assert.IsTrue(_a1 <= _a2b2);
		Assert.IsTrue(_a1b2 <= _a1b2);
		Assert.IsFalse(_a1b2 <= _a1b1);
		Assert.Throws<InvalidOperationException>(() => TestHelper.Consume(_a1b2 <= _a2b1));
		Assert.Throws<InvalidOperationException>(() => TestHelper.Consume(_a1 <= _b1));
	}

	[Test]
	public void greater_operator() {
		Assert.IsFalse(_a1 > _a1b1);
		Assert.IsFalse(_a1 > _a1b2);
		Assert.IsFalse(_a1 > _a2b2);
		Assert.IsFalse(_a1b2 > _a1b2);
		Assert.IsTrue(_a1b2 > _a1b1);
		Assert.Throws<InvalidOperationException>(() => TestHelper.Consume(_a1b2 > _a2b1));
		Assert.Throws<InvalidOperationException>(() => TestHelper.Consume(_a1 > _b1));
	}

	[Test]
	public void greater_or_equal_operator() {
		Assert.IsFalse(_a1 >= _a1b1);
		Assert.IsFalse(_a1 >= _a1b2);
		Assert.IsFalse(_a1 >= _a2b2);
		Assert.IsTrue(_a1b2 >= _a1b2);
		Assert.IsTrue(_a1b2 >= _a1b1);
		Assert.Throws<InvalidOperationException>(() => TestHelper.Consume(_a1b2 >= _a2b1));
		Assert.Throws<InvalidOperationException>(() => TestHelper.Consume(_a1 >= _b1));
	}
}
#pragma warning restore 1718

