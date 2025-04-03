// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Services.RequestManager.Managers;
using EventStore.Core.Tests.Fakes;
using EventStore.Core.Tests.Helpers;
using EventStore.Core.TransactionLog.LogRecords;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.RequestManagement.TransactionMgr;

[TestFixture]
public class when_transaction_single_write_completes_successfully : RequestManagerSpecification<TransactionWrite> {

	private long _transactionId = 1000;
	private long _event1Position = 1500;
	
	protected override TransactionWrite OnManager(FakePublisher publisher) {
		return new TransactionWrite(
		 	publisher,
			PrepareTimeout,
			Envelope,
			InternalCorrId,
			ClientCorrId,
			new[] { DummyEvent()},
		    _transactionId,
			CommitSource);
	}

	protected override IEnumerable<Message> WithInitialMessages() {
		yield return new StorageMessage.PrepareAck(InternalCorrId, _event1Position, PrepareFlags.Data);
	}

	protected override Message When() {
		return new ReplicationTrackingMessage.ReplicatedTo(_event1Position);
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
