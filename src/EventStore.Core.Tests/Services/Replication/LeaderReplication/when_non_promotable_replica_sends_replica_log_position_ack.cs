using EventStore.Core.Messages;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.Replication.LeaderReplication {
	[TestFixture]
	public class when_non_promotable_replica_sends_replica_log_position_ack : with_replication_service {
		private long _logPosition;

		public override void When() {
			_logPosition = 4000;
			Service.Handle(new ReplicationMessage.ReplicaLogPositionAck(ReadOnlyReplicaId, _logPosition, _logPosition));
		}

		[Test]
		public void replica_Log_written_to_should_not_be_published() {
			Assert.AreEqual(0, ReplicaWriteAcks.Count);
		}
	}
}
