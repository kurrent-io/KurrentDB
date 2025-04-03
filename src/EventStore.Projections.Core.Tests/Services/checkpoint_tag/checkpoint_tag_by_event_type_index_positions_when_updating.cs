// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.Linq;
using EventStore.Core.Data;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.checkpoint_tag;

[TestFixture]
public class checkpoint_tag_by_event_type_index_positions_when_updating {
	private readonly CheckpointTag _a1b1 = CheckpointTag.FromEventTypeIndexPositions(
		1, new TFPos(100, 50), new Dictionary<string, long> {{"a", 1}, {"b", 1}});

	[Test]
	public void updated_tf_only_position_is_correct() {
		var updated = _a1b1.UpdateEventTypeIndexPosition(new TFPos(200, 50));
		Assert.AreEqual(1, updated.Streams["a"]);
		Assert.AreEqual(new TFPos(200, 50), updated.Position);
	}

	[Test]
	public void updated_tf_position_is_correct() {
		var updated = _a1b1.UpdateEventTypeIndexPosition(new TFPos(200, 50), "a", 2);
		Assert.AreEqual(2, updated.Streams["a"]);
		Assert.AreEqual(new TFPos(200, 50), updated.Position);
	}

	[Test]
	public void other_stream_position_is_correct() {
		var updated = _a1b1.UpdateEventTypeIndexPosition(new TFPos(200, 50), "a", 2);
		Assert.AreEqual(1, updated.Streams["b"]);
	}

	[Test]
	public void streams_are_correct() {
		var updated = _a1b1.UpdateEventTypeIndexPosition(new TFPos(200, 50), "a", 2);
		Assert.AreEqual(2, updated.Streams.Count);
		Assert.IsTrue(updated.Streams.Any(v => v.Key == "a"));
		Assert.IsTrue(updated.Streams.Any(v => v.Key == "b"));
	}
}
