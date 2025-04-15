// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Projections.Core.Services.Management;
using NUnit.Framework;

namespace KurrentDB.Projections.Core.Tests.Services.projections_manager;

[TestFixture]
public class VerifyPersistentStateRulesForDeletingStreams {
	[Test]
	[TestCase(true, true, true)]
	[TestCase(false, true, false)]
	[TestCase(true, false, false)]
	[TestCase(false, false, false)]
	public void EmitStreamNeedsDeletedAsExpected(bool emitEnabled, bool deleteEmitStreams, bool expectedResult) {
		ManagedProjection.PersistedState persistedState = new ManagedProjection.PersistedState();

		persistedState.EmitEnabled = emitEnabled;
		persistedState.DeleteEmittedStreams = deleteEmitStreams;

		Assert.IsTrue(persistedState.EmitStreamNeedsDeleted() == expectedResult);
	}

	[Test]
	[TestCase(true, true, false)]
	[TestCase(false, true, true)]
	[TestCase(true, false, false)]
	[TestCase(false, false, false)]
	public void CheckpointStreamNeedsDeletedAsExpected(bool checkPointsDisabled, bool deleteCheckpointStreams, bool expectedResult) {
		ManagedProjection.PersistedState persistedState = new ManagedProjection.PersistedState();

		persistedState.CheckpointsDisabled = checkPointsDisabled;
		persistedState.DeleteCheckpointStream = deleteCheckpointStreams;

		Assert.IsTrue(persistedState.CheckpointStreamNeedsDeleted() == expectedResult);
	}
}
