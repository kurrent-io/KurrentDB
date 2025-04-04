using System;
using EventStore.Core.Messaging;

namespace EventStore.Projections.Core.Messages {
	public static partial class ReaderCoreServiceMessage {
		[DerivedMessage(ProjectionMessage.ReaderCoreService)]
		public partial class StartReader : Message {
			public Guid InstanceCorrelationId { get; }

			public StartReader(Guid instanceCorrelationId) {
				InstanceCorrelationId = instanceCorrelationId;
			}
		}

		[DerivedMessage(ProjectionMessage.ReaderCoreService)]
		public partial class StopReader : Message {
			public Guid QueueId { get; }

			public StopReader(Guid queueId) {
				QueueId = queueId;
			}
		}
	}
}
