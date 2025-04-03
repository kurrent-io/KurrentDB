// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using NUnit.Framework;

#pragma warning disable 1718 // allow a == a comparison

namespace EventStore.Projections.Core.Tests.Services.checkpoint_tag;

[TestFixture]
public class checkpoint_tag_by_catalog_stream {
	private readonly CheckpointTag _a = CheckpointTag.FromByStreamPosition(0, "catalog", 1, "data", 10, 12345);
	private readonly CheckpointTag _b = CheckpointTag.FromByStreamPosition(0, "catalog", 1, "data", 20, 12345);
	private readonly CheckpointTag _c = CheckpointTag.FromByStreamPosition(0, "catalog", 2, "data2", 20, 12345);

	[Test]
	public void equal_equals() {
		Assert.IsTrue(_a.Equals(_a));
	}

	[Test]
	public void equal_operator() {
		Assert.IsTrue(_b == _b);
	}

	[Test]
	public void less_operator() {
		Assert.IsTrue(_a < _b);
	}

	[Test]
	public void less_or_equal_operator() {
		Assert.IsTrue(_a <= _b);
		Assert.IsTrue(_c <= _c);
	}

	[Test]
	public void greater_operator() {
		Assert.IsTrue(_b > _a);
	}

	[Test]
	public void greater_or_equal_operator() {
		Assert.IsTrue(_b >= _a);
		Assert.IsTrue(_c >= _c);
	}
}
#pragma warning restore 1718

