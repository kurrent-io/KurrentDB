// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Services.RequestManager.Managers;
using KurrentDB.Core.Tests.Fakes;
using KurrentDB.Core.TransactionLog.LogRecords;
using NUnit.Framework;

namespace KurrentDB.Core.Tests.Services.RequestManagement.WriteStreamMgr;

[TestFixture]
public class when_write_stream_gets_timeout_after_cluster_commit : RequestManagerSpecification<WriteEvents> {
	private long _prepareLogPosition = 100;
	private long _commitPosition = 100;
	protected override WriteEvents OnManager(FakePublisher publisher) {
		return WriteEvents.ForSingleStream(
			publisher,
			CommitTimeout,
			Envelope,
			InternalCorrId,
			ClientCorrId,
			"test123",
			ExpectedVersion.Any,
			new(DummyEvent()),
			CommitSource);
	}

	protected override IEnumerable<Message> WithInitialMessages() {
		yield return new StorageMessage.UncommittedPrepareChased(InternalCorrId, _prepareLogPosition, PrepareFlags.SingleWrite | PrepareFlags.Data);
		yield return StorageMessage.CommitIndexed.ForSingleStream(InternalCorrId, _commitPosition, 1);
		yield return new ReplicationTrackingMessage.ReplicatedTo(_commitPosition);
	}

	protected override Message When() {
		return new StorageMessage.RequestManagerTimerTick(DateTime.UtcNow + TimeSpan.FromMinutes(1));
	}

	[Test]
	public void no_additional_messages_are_published() {
		Assert.That(Produced.Count == 0);
	}
	[Test]
	public void the_envelope_has_no_additional_replies() {
		Assert.AreEqual(0, Envelope.Replies.Count);
	}
}
