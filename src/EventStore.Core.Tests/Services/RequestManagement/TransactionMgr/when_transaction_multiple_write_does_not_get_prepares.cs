// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.Linq;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Services.RequestManager.Managers;
using EventStore.Core.Tests.Fakes;
using EventStore.Core.TransactionLog.LogRecords;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.RequestManagement.TransactionMgr;

[TestFixture]
public class when_transaction_multiple_write_does_not_get_prepares : RequestManagerSpecification<TransactionWrite> {

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
		yield return new StorageMessage.PrepareAck(InternalCorrId, _event1Position, PrepareFlags.Data);
		yield return new StorageMessage.PrepareAck(InternalCorrId, _event2Position, PrepareFlags.Data);
	}

	protected override Message When() {
		return new ReplicationTrackingMessage.ReplicatedTo(_event3Position);
	}

	[Test]
	public void successful_request_message_is_not_published() {
		Assert.That(!Produced.Any());
	}

	[Test]
	public void the_envelope_is_not_replied_to_with_success() {
		Assert.That(!Envelope.Replies.Any());
	}
}
