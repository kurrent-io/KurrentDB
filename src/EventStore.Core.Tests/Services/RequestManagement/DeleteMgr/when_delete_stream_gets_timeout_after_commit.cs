// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Services.RequestManager.Managers;
using EventStore.Core.Tests.Fakes;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.RequestManagement.DeleteMgr;

public class when_delete_stream_gets_timeout_after_commit : RequestManagerSpecification<DeleteStream> {
	private long _commitPosition = 3000;
	protected override DeleteStream OnManager(FakePublisher publisher) {
		return new DeleteStream(
			publisher,
			CommitTimeout,
			Envelope,
			InternalCorrId,
			ClientCorrId,
			"test123",
			ExpectedVersion.Any,
			false,
			CommitSource);
	}

	protected override IEnumerable<Message> WithInitialMessages() {
		yield return new StorageMessage.CommitIndexed(InternalCorrId, _commitPosition, 500, 1, 1);
		yield return new ReplicationTrackingMessage.ReplicatedTo(_commitPosition);
	}

	protected override Message When() {
		return new StorageMessage.RequestManagerTimerTick(DateTime.UtcNow + TimeSpan.FromMinutes(1));
	}

	[Test]
	public void no_additional_messages_are_published() {
		Assert.That(!Produced.Any());
	}
	[Test]
	public void the_envelope_has_single_successful_reply() {
		Assert.AreEqual(0, Envelope.Replies.Count);
	}
}
