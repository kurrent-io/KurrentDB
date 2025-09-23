// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Tests;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using KurrentDB.Projections.Core.Services.Processing.Partitioning;
using NUnit.Framework;

namespace KurrentDB.Projections.Core.Tests.Services.core_projection.checkpoint_manager;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class checkpoint_manager_with_partition<TLogFormat, TStreamId>
	: TestFixtureWithCoreProjectionCheckpointManager<TLogFormat, TStreamId> {
	[Test]
	public void when_loading_partition_state_for_a_partition() {
		const string checkpointMetadata
			= """
			  {
			  	"$v": "1:-1:0:1",
			  	"$s": {
			  		"$ce-Evnt": 0
			  	}
			  }
			  """;
		var state = new PartitionState("{\"foo\":1}", "{\"bar\":1}", CheckpointTag.Empty);
		var serializedState = state.Serialize();
		const string partition = "abc";
		ExistingEvent(_namingBuilder.MakePartitionCheckpointStreamName(partition), "$Checkpoint", checkpointMetadata, serializedState);
		_manager.BeginLoadPartitionStateAt(partition, CheckpointTag.Empty, s => {
			Assert.AreEqual(serializedState, s.Serialize());
		});
	}
}
