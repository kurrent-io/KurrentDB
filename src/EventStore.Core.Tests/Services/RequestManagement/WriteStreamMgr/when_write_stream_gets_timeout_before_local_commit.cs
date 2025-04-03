// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Tests.Fakes;
using NUnit.Framework;
using EventStore.Core.Services.RequestManager.Managers;

namespace EventStore.Core.Tests.Services.RequestManagement.WriteStreamMgr;

[TestFixture]
public class when_write_stream_gets_timeout_before_local_commit : RequestManagerSpecification<WriteEvents> {
	protected override WriteEvents OnManager(FakePublisher publisher) {
		return new WriteEvents(
			publisher, 
			CommitTimeout, 
			Envelope,
			InternalCorrId,
			ClientCorrId,
			"test123",
			ExpectedVersion.Any,
			new[] {DummyEvent()},
			CommitSource);
	}

	protected override IEnumerable<Message> WithInitialMessages() {
		yield break;			
	}

	protected override Message When() {
		return new StorageMessage.RequestManagerTimerTick(DateTime.UtcNow + CommitTimeout + CommitTimeout);
	}

	[Test]
	public void request_completed_as_failed_published() {
		Assert.AreEqual(1, Produced.Count);
		Assert.AreEqual(false, ((StorageMessage.RequestCompleted)Produced[0]).Success);
	}

	[Test]
	public void the_envelope_is_replied_to() {
		Assert.AreEqual(1, Envelope.Replies.Count);

	}
}
