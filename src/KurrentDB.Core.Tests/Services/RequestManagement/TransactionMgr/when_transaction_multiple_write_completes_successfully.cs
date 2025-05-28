// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Services.RequestManager.Managers;
using KurrentDB.Core.Tests.Fakes;
using KurrentDB.Core.Tests.Helpers;
using KurrentDB.Core.TransactionLog.LogRecords;
using NUnit.Framework;

namespace KurrentDB.Core.Tests.Services.RequestManagement.TransactionMgr;

[TestFixture]
public class when_transaction_multiple_write_completes_successfully : RequestManagerSpecification<TransactionWrite> {

	private long _transactionId = 1000;
	private long _event1Position = 1500;
	private long _event2Position = 2000;
	private long _event3Position = 2500;

	protected override TransactionWrite OnManager(FakePublisher publisher) {
		return new TransactionWrite(
		 	publisher,
			PrepareTimeout,
			Envelope,
			InternalCorrId,
			ClientCorrId,
			new[] { DummyEvent(), DummyEvent(), DummyEvent() },
			_transactionId,
			CommitSource);
	}

	protected override IEnumerable<Message> WithInitialMessages() {
		yield return new StorageMessage.UncommittedPrepareChased(InternalCorrId, _event1Position, PrepareFlags.Data);
		yield return new StorageMessage.UncommittedPrepareChased(InternalCorrId, _event2Position, PrepareFlags.Data);
		yield return new StorageMessage.UncommittedPrepareChased(InternalCorrId, _event3Position, PrepareFlags.Data);
	}

	protected override Message When() {
		return new ReplicationTrackingMessage.ReplicatedTo(_event3Position);
	}

	[Test]
	public void successful_request_message_is_published() {
		Assert.That(Produced.ContainsSingle<StorageMessage.RequestCompleted>(
			x => x.CorrelationId == InternalCorrId && x.Success));
	}

	[Test]
	public void the_envelope_is_replied_to_with_success() {
		Assert.That(Envelope.Replies.ContainsSingle<ClientMessage.TransactionWriteCompleted>(
			x =>
				x.CorrelationId == ClientCorrId &&
				x.Result == OperationResult.Success &&
				x.TransactionId == _transactionId));
	}
}
