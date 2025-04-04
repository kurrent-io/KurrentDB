using System;
using System.Collections.Generic;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Services.RequestManager.Managers;
using EventStore.Core.Tests.Fakes;
using EventStore.Core.Tests.Helpers;
using EventStore.Core.TransactionLog.LogRecords;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.RequestManagement.TransactionMgr {
	public class when_transaction_commit_gets_timeout_before_cluster_commit : RequestManagerSpecification<TransactionCommit> {

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
			yield return new StorageMessage.PrepareAck(InternalCorrId, 100, PrepareFlags.Data);
			yield return new StorageMessage.PrepareAck(InternalCorrId, 200, PrepareFlags.Data);
			yield return new StorageMessage.CommitAck(InternalCorrId, 300, 100, 1, 2);
		}

		protected override Message When() {
			return new StorageMessage.RequestManagerTimerTick(
				DateTime.UtcNow + PrepareTimeout + CommitTimeout + TimeSpan.FromMinutes(5));
		}

		[Test]
		public void failed_request_message_is_published() {
			Assert.That(Produced.ContainsSingle<StorageMessage.RequestCompleted>(
				x => x.CorrelationId == InternalCorrId && x.Success == false));
		}

		[Test]
		public void the_envelope_is_replied_to_with_failure() {
			Assert.That(Envelope.Replies.ContainsSingle<ClientMessage.TransactionCommitCompleted>(
				x => x.CorrelationId == ClientCorrId && x.Result == OperationResult.CommitTimeout));
		}
	}
}
