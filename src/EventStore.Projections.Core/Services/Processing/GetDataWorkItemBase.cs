using System;
using EventStore.Core.Bus;

namespace EventStore.Projections.Core.Services.Processing {
	abstract class GetDataWorkItemBase : WorkItem {
		protected readonly IPublisher _publisher;
		protected readonly string _partition;
		protected Guid _correlationId;
		protected Guid _projectionId;
		private readonly IProjectionPhaseStateManager _projection;
		private PartitionState _state;
		private CheckpointTag _lastProcessedCheckpointTag;

		protected GetDataWorkItemBase(
			IPublisher publisher,
			Guid correlationId,
			Guid projectionId,
			IProjectionPhaseStateManager projection,
			string partition)
			: base(null) {
			if (partition == null)
				throw new ArgumentNullException("partition");
			_publisher = publisher;
			_partition = partition;
			_correlationId = correlationId;
			_projectionId = projectionId;
			_projection = projection;
		}

		protected override void GetStatePartition() {
			NextStage(_partition);
		}

		protected override void Load(CheckpointTag checkpointTag) {
			_lastProcessedCheckpointTag = _projection.LastProcessedEventPosition;
			_projection.BeginGetPartitionStateAt(
				_partition,
				_lastProcessedCheckpointTag,
				LoadCompleted,
				lockLoaded: false);
		}

		private void LoadCompleted(PartitionState state) {
			_state = state;
			NextStage();
		}

		protected override void WriteOutput() {
			Reply(_state, _lastProcessedCheckpointTag);
			NextStage();
		}

		protected abstract void Reply(PartitionState state, CheckpointTag checkpointTag);
	}
}
