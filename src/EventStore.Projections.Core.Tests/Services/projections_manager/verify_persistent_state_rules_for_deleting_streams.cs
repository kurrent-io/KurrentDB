namespace EventStore.Projections.Core.Tests.Services.projections_manager {
	using Core.Services.Management;
	using NUnit.Framework;

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
}
