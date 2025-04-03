// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Linq;
using EventStore.Core.Tests;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services.Processing;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.core_projection.multi_phase;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
class when_completing_phase1_of_a_multiphase_projection<TLogFormat, TStreamId> : specification_with_multi_phase_core_projection<TLogFormat, TStreamId> {
	protected override void When() {
		_coreProjection.Start();
		Phase1.Complete();
	}

	[Test]
	public void stops_phase1_checkpoint_manager() {
		Assert.IsTrue(Phase1CheckpointManager.Stopped_);
	}

	[Test]
	public void initializes_phase2() {
		Assert.IsTrue(Phase2.InitializedFromCheckpoint);
	}

	[Test]
	public void updates_checkpoint_tag_phase() {
		Assert.AreEqual(1, _coreProjection.LastProcessedEventPosition.Phase);
	}

	[Test]
	public void publishes_subscribe_message() {
		Assert.AreEqual(1, Phase2.SubscribeInvoked);
	}
}
