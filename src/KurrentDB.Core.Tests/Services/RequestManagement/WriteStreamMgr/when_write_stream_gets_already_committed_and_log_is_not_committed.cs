// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Services.RequestManager.Managers;
using KurrentDB.Core.Tests.Fakes;
using KurrentDB.Core.Tests.Helpers;
using NUnit.Framework;

namespace KurrentDB.Core.Tests.Services.RequestManagement.WriteStreamMgr;

[TestFixture]
public class when_write_stream_gets_already_committed_and_log_is_not_committed : RequestManagerSpecification<WriteEvents> {
	private long _commitLogPosition = 1000;
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
		yield return new ReplicationTrackingMessage.ReplicatedTo(_commitLogPosition - 1);
	}

	protected override Message When() {
		return StorageMessage.AlreadyCommitted.ForSingleStream(InternalCorrId, "test123", 0, 1, _commitLogPosition);
	}

	[Test]
	public void successful_request_message_is_published() {
		Assert.That(Produced.ContainsSingle<StorageMessage.RequestCompleted>(
			x => x.CorrelationId == InternalCorrId && x.Success));
	}

	[Test]
	public void the_envelope_is_replied_to_with_success() {
		Assert.That(Envelope.Replies.ContainsSingle<ClientMessage.WriteEventsCompleted>(
			x => x.CorrelationId == ClientCorrId && x.Result == OperationResult.Success));
	}
}
