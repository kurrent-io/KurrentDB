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
public class checkpoint_tag_by_event_type_index_positions {
	private readonly CheckpointTag _a1 = CheckpointTag.FromEventTypeIndexPositions(
		1, new TFPos(100, 50), new Dictionary<string, long> {{"a", 1}});

	private readonly CheckpointTag _a1_prime = CheckpointTag.FromEventTypeIndexPositions(
		1, new TFPos(100, 50), new Dictionary<string, long> {{"a", 0}});

	private readonly CheckpointTag _b1 = CheckpointTag.FromEventTypeIndexPositions(
		1, new TFPos(200, 150), new Dictionary<string, long> {{"b", 1}});

	private readonly CheckpointTag _a1b1 = CheckpointTag.FromEventTypeIndexPositions(
		1, new TFPos(300, 250), new Dictionary<string, long> {{"a", 1}, {"b", 1}});

	private readonly CheckpointTag _a2b1 = CheckpointTag.FromEventTypeIndexPositions(
		1, new TFPos(400, 350), new Dictionary<string, long> {{"a", 2}, {"b", 1}});

	private readonly CheckpointTag _a2b1_after = CheckpointTag.FromEventTypeIndexPositions(
		1, new TFPos(430, 420), new Dictionary<string, long> {{"a", 2}, {"b", 1}});

	private readonly CheckpointTag _a1b2 = CheckpointTag.FromEventTypeIndexPositions(
		1, new TFPos(500, 450), new Dictionary<string, long> {{"a", 1}, {"b", 2}});

	private readonly CheckpointTag _a2b2 = CheckpointTag.FromEventTypeIndexPositions(
		1, new TFPos(600, 550), new Dictionary<string, long> {{"a", 2}, {"b", 2}});

	[Test]
	public void equal_equals() {
		Assert.IsTrue(_a1.Equals(_a1));
	}

	[Test]
	public void equal_but_different_index_positions_still_equals() {
		Assert.IsTrue(_a1.Equals(_a1_prime));
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
		Assert.IsTrue(_a2b1 < _a2b1_after);
		Assert.IsFalse(_a1b2 < _a1b2);
		Assert.IsFalse(_a1b2 < _a1b1);
		Assert.IsFalse(_a1b2 < _a2b1);
		Assert.IsTrue(_a1 < _b1);
	}

	[Test]
	public void less_or_equal_operator() {
		Assert.IsTrue(_a1 <= _a1b1);
		Assert.IsTrue(_a1 <= _a1b2);
		Assert.IsTrue(_a1 <= _a2b2);
		Assert.IsTrue(_a2b1 <= _a2b1_after);
		Assert.IsTrue(_a1b2 <= _a1b2);
		Assert.IsFalse(_a1b2 <= _a1b1);
		Assert.IsFalse(_a1b2 <= _a2b1);
		Assert.IsTrue(_a1 <= _b1);
	}

	[Test]
	public void greater_operator() {
		Assert.IsFalse(_a1 > _a1b1);
		Assert.IsFalse(_a1 > _a1b2);
		Assert.IsFalse(_a1 > _a2b2);
		Assert.IsFalse(_a2b1 > _a2b1_after);
		Assert.IsTrue(_a2b1_after > _a2b1);
		Assert.IsFalse(_a1b2 > _a1b2);
		Assert.IsTrue(_a1b2 > _a1b1);
		Assert.IsTrue(_a1b2 > _a2b1);
		Assert.IsFalse(_a1 > _b1);
	}

	[Test]
	public void greater_or_equal_operator() {
		Assert.IsFalse(_a1 >= _a1b1);
		Assert.IsFalse(_a1 >= _a1b2);
		Assert.IsFalse(_a1 >= _a2b2);
		Assert.IsFalse(_a2b1 >= _a2b1_after);
		Assert.IsTrue(_a2b1_after >= _a2b1);
		Assert.IsTrue(_a1b2 >= _a1b2);
		Assert.IsTrue(_a1b2 >= _a1b1);
		Assert.IsTrue(_a1b2 >= _a2b1);
		Assert.IsFalse(_a1 >= _b1);
	}
}
#pragma warning restore 1718

