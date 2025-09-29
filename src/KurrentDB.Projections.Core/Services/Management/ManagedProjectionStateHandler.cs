// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Projections.Core.Messages;

namespace KurrentDB.Projections.Core.Services.Management;

public static class ManagedProjectionStateHandler {
	internal abstract class BaseHandler(ManagedProjection managedProjection) {
		protected readonly ManagedProjection ManagedProjection = managedProjection;

		private void Unexpected(string message) {
			ManagedProjection.Fault($"{message} in {GetType().Name}");
		}

		protected void SetFaulted(string reason) {
			ManagedProjection.Fault(reason);
		}

		protected internal virtual void Started() {
			Unexpected("Unexpected 'STARTED' message");
		}

		protected internal virtual void Stopped(CoreProjectionStatusMessage.Stopped message) {
			Unexpected("Unexpected 'STOPPED' message");
		}

		protected internal virtual void Faulted(CoreProjectionStatusMessage.Faulted message) {
			Unexpected("Unexpected 'FAULTED' message");
		}

		protected internal virtual void Prepared(CoreProjectionStatusMessage.Prepared message) {
			Unexpected("Unexpected 'PREPARED' message");
		}
	}

	internal class Aborted(ManagedProjection managedProjection) : BaseHandler(managedProjection);

	internal class AbortingState(ManagedProjection managedProjection) : BaseHandler(managedProjection) {
		protected internal override void Stopped(CoreProjectionStatusMessage.Stopped message) {
			ManagedProjection.SetState(ManagedProjectionState.Aborted);
			ManagedProjection.PrepareOrWriteStartOrLoadStopped();
		}

		protected internal override void Faulted(CoreProjectionStatusMessage.Faulted message) {
			ManagedProjection.SetState(ManagedProjectionState.Aborted);
			ManagedProjection.PrepareOrWriteStartOrLoadStopped();
		}
	}

	internal class CompletedState(ManagedProjection managedProjection) : BaseHandler(managedProjection);

	internal class CreatingLoadingLoadedState(ManagedProjection managedProjection) : BaseHandler(managedProjection);

	internal class DeletingState : BaseHandler {
		public DeletingState(ManagedProjection managedProjection) : base(managedProjection) {
			ManagedProjection.DeleteProjectionStreams();
		}
	}

	internal class FaultedState(ManagedProjection managedProjection) : BaseHandler(managedProjection);

	internal class LoadingStateState(ManagedProjection managedProjection) : BaseHandler(managedProjection) {
		protected internal override void Stopped(CoreProjectionStatusMessage.Stopped message) {
			ManagedProjection.SetState(ManagedProjectionState.Stopped);
			ManagedProjection.PrepareOrWriteStartOrLoadStopped();
		}

		protected internal override void Faulted(CoreProjectionStatusMessage.Faulted message) {
			SetFaulted(message.FaultedReason);
			ManagedProjection.PrepareOrWriteStartOrLoadStopped();
		}
	}

	internal class PreparedState(ManagedProjection managedProjection) : BaseHandler(managedProjection);

	internal class PreparingState(ManagedProjection managedProjection) : BaseHandler(managedProjection) {
		protected internal override void Faulted(CoreProjectionStatusMessage.Faulted message) {
			SetFaulted(message.FaultedReason);
			ManagedProjection.PersistedProjectionState.SourceDefinition = null;
			ManagedProjection.WriteStartOrLoadStopped();
		}

		protected internal override void Prepared(CoreProjectionStatusMessage.Prepared message) {
			ManagedProjection.SetState(ManagedProjectionState.Prepared);
			ManagedProjection.PersistedProjectionState.SourceDefinition = message.SourceDefinition;
			ManagedProjection.Prepared = true;
			ManagedProjection.Created = true;
			ManagedProjection.WriteStartOrLoadStopped();
		}
	}

	internal class RunningState(ManagedProjection managedProjection) : BaseHandler(managedProjection) {
		protected internal override void Faulted(CoreProjectionStatusMessage.Faulted message) {
			SetFaulted(message.FaultedReason);
		}

		protected internal override void Started() {
			// do nothing - may mean second pahse started
			//TODO: stop sending second Started
		}

		protected internal override void Stopped(CoreProjectionStatusMessage.Stopped message) {
			if (message.Completed)
				ManagedProjection.SetState(ManagedProjectionState.Completed);
			else
				base.Stopped(message);
		}
	}

	internal class StartingState(ManagedProjection managedProjection) : BaseHandler(managedProjection) {
		protected internal override void Started() {
			ManagedProjection.SetState(ManagedProjectionState.Running);
			ManagedProjection.StartCompleted();
		}

		protected internal override void Faulted(CoreProjectionStatusMessage.Faulted message) {
			SetFaulted(message.FaultedReason);
			ManagedProjection.StartCompleted();
		}
	}

	internal class StoppedState(ManagedProjection managedProjection) : BaseHandler(managedProjection);

	internal class StoppingState(ManagedProjection managedProjection) : BaseHandler(managedProjection) {
		protected internal override void Stopped(CoreProjectionStatusMessage.Stopped message) {
			ManagedProjection.SetState(message.Completed ? ManagedProjectionState.Completed : ManagedProjectionState.Stopped);
			ManagedProjection.PrepareOrWriteStartOrLoadStopped();
		}

		protected internal override void Faulted(CoreProjectionStatusMessage.Faulted message) {
			SetFaulted(message.FaultedReason);
			ManagedProjection.PrepareOrWriteStartOrLoadStopped();
		}
	}
}
