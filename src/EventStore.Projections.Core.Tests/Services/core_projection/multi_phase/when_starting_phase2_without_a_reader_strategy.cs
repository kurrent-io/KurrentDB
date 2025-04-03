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
class when_starting_phase2_without_a_reader_strategy<TLogFormat, TStreamId> : specification_with_multi_phase_core_projection<TLogFormat, TStreamId> {
	protected override FakeReaderStrategy GivenPhase2ReaderStrategy() {
		return null;
	}

	protected override void When() {
		_coreProjection.Start();
		Phase1.Complete();
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
	public void starts_processing_phase2() {
		Assert.AreEqual(1, Phase2.ProcessEventInvoked);
	}
}
