using EventStore.Projections.Core.Messages;

namespace EventStore.Projections.Core.Services.Processing {
	class PartitionDeletedWorkItem : WorkItem {
		private readonly EventReaderSubscriptionMessage.PartitionDeleted _message;
		private readonly string _partition;
		private readonly IEventProcessingProjectionPhase _projection;
		private EventProcessedResult _eventProcessedResult;

		public PartitionDeletedWorkItem(
			IEventProcessingProjectionPhase projection, EventReaderSubscriptionMessage.PartitionDeleted message)
			: base(null) {
			_partition = message.Partition;
			_projection = projection;
			_message = message;
			_requiresRunning = true;
		}

		protected override void GetStatePartition() {
			NextStage(_partition);
		}

		protected override void Load(CheckpointTag checkpointTag) {
			// we load partition state even if stopping etc.  should we skip?
			_projection.BeginGetPartitionStateAt(_partition, _message.CheckpointTag, LoadCompleted, lockLoaded: true);
		}

		private void LoadCompleted(PartitionState state) {
			NextStage();
		}

		protected override void ProcessEvent() {
			if (_partition == null) {
				NextStage();
				return;
			}

			var eventProcessedResult = _projection.ProcessPartitionDeleted(_partition, _message.CheckpointTag);
			if (eventProcessedResult != null)
				SetEventProcessedResult(eventProcessedResult);
			NextStage();
		}

		protected override void WriteOutput() {
			if (_partition == null) {
				NextStage();
				return;
			}

			_projection.FinalizeEventProcessing(_eventProcessedResult, _message.CheckpointTag, _message.Progress);
			NextStage();
		}

		private void SetEventProcessedResult(EventProcessedResult eventProcessedResult) {
			_eventProcessedResult = eventProcessedResult;
		}
	}
}
