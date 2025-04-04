using System;
using EventStore.Projections.Core.Messages;

namespace EventStore.Projections.Core.Services.Processing {
	public class PhasePositionTagger : PositionTagger {
		public PhasePositionTagger(int phase) : base(phase) {
		}

		public override bool IsMessageAfterCheckpointTag(
			CheckpointTag previous, ReaderSubscriptionMessage.CommittedEventDistributed committedEvent) {
			throw new NotSupportedException();
		}

		public override CheckpointTag MakeCheckpointTag(
			CheckpointTag previous, ReaderSubscriptionMessage.CommittedEventDistributed committedEvent) {
			throw new NotSupportedException();
		}

		public override CheckpointTag MakeCheckpointTag(CheckpointTag previous,
			ReaderSubscriptionMessage.EventReaderPartitionEof partitionEof) {
			throw new NotSupportedException();
		}

		public override CheckpointTag MakeCheckpointTag(CheckpointTag previous,
			ReaderSubscriptionMessage.EventReaderPartitionDeleted partitionDeleted) {
			throw new NotSupportedException();
		}

		public override CheckpointTag MakeZeroCheckpointTag() {
			return CheckpointTag.FromPhase(Phase, completed: false);
		}

		public override bool IsCompatible(CheckpointTag checkpointTag) {
			return checkpointTag.Mode_ == CheckpointTag.Mode.Phase;
		}

		public override CheckpointTag AdjustTag(CheckpointTag tag) {
			if (tag.Phase < Phase)
				return tag;
			if (tag.Phase > Phase)
				throw new ArgumentException(
					string.Format("Invalid checkpoint tag phase.  Expected less or equal to: {0} Was: {1}", Phase,
						tag.Phase), "tag");

			if (tag.Mode_ == CheckpointTag.Mode.Phase) {
				return tag;
			}

			throw new NotSupportedException("Conversion to phase based checkpoint tag is not supported");
		}
	}
}
