using System.Collections.Generic;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Services.RequestManager.Managers;
using EventStore.Core.Tests.Fakes;
using EventStore.Core.Tests.Helpers;
using EventStore.Core.TransactionLog.LogRecords;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.RequestManagement.TransactionMgr {
	[TestFixture]
	public class when_transaction_commit_completes_successfully : RequestManagerSpecification<TransactionCommit> {
		private long _commitPosition = 3000;
		private long _transactionPosition = 1000;
		private int transactionId = 2341;

		protected override TransactionCommit OnManager(FakePublisher publisher) {
			return new TransactionCommit(
				publisher,
				PrepareTimeout,
				CommitTimeout,
				Envelope,
				InternalCorrId,
				ClientCorrId,
				transactionId,
				CommitSource);
		}

		protected override IEnumerable<Message> WithInitialMessages() {
			yield return new StorageMessage.PrepareAck(InternalCorrId, _transactionPosition, PrepareFlags.TransactionEnd);
			yield return new StorageMessage.CommitAck(InternalCorrId, _commitPosition, _transactionPosition, 1, 3);
			yield return new ReplicationTrackingMessage.ReplicatedTo(_commitPosition);
		}

		protected override Message When() {
			return new StorageMessage.CommitIndexed(InternalCorrId, _commitPosition, _transactionPosition, 0, 0);
		}

		[Test]
		public void successful_request_message_is_published() {
			Assert.That(Produced.ContainsSingle<StorageMessage.RequestCompleted>(
				x => x.CorrelationId == InternalCorrId && x.Success));
		}

		[Test]
		public void the_envelope_is_replied_to_with_success() {
			Assert.That(Envelope.Replies.ContainsSingle<ClientMessage.TransactionCommitCompleted>(
				x => x.CorrelationId == ClientCorrId && x.Result == OperationResult.Success));
		}
	}
}
