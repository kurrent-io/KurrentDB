// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Data;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using NUnit.Framework;
using ResolvedEvent = EventStore.Projections.Core.Services.Processing.ResolvedEvent;

namespace EventStore.Projections.Core.Tests.Services.Jint;

[TestFixture]
public class when_partitioning_by_custom_rule_returning_a_number : TestFixtureWithInterpretedProjection {
	protected override void Given() {
		_projection = @"
                fromAll().partitionBy(function(event){
                    return event.body.index;
                }).when({$any:function(event, state) {
                    return {};
                }});
            ";
	}

	[Test]
	public void get_state_partition_returns_correct_result() {
		var result = _stateHandler.GetStatePartition(
			CheckpointTag.FromPosition(0, 100, 50), "category",
			new ResolvedEvent(
				"stream1", 0, "stream1", 0, false, new TFPos(100, 50), Guid.NewGuid(), "type1", true,
				@"{""index"": 42}", "metadata"));

		Assert.AreEqual("42", result);
	}

}
