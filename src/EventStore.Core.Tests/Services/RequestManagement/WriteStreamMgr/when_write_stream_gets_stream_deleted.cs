// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System.Collections.Generic;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Services.RequestManager.Managers;
using EventStore.Core.Tests.Fakes;
using EventStore.Core.Tests.Helpers;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.RequestManagement.WriteStreamMgr;

[TestFixture]
public class when_write_stream_gets_stream_deleted : RequestManagerSpecification<WriteEvents> {
	protected override WriteEvents OnManager(FakePublisher publisher) {
		return new WriteEvents(
		publisher,
		CommitTimeout,
		Envelope,
		InternalCorrId,
		ClientCorrId,
		"test123",
		ExpectedVersion.Any,
		new[] { DummyEvent() },
		CommitSource);
	}

	protected override IEnumerable<Message> WithInitialMessages() {
		yield break;
	}

	protected override Message When() {
		return new StorageMessage.StreamDeleted(InternalCorrId);
	}

	[Test]
	public void failed_request_message_is_published() {
		Assert.That(Produced.ContainsSingle<StorageMessage.RequestCompleted>(
			x => x.CorrelationId == InternalCorrId && x.Success == false));
	}

	[Test]
	public void the_envelope_is_replied_to_with_failure() {
		Assert.That(Envelope.Replies.ContainsSingle<ClientMessage.WriteEventsCompleted>(
			x => x.CorrelationId == ClientCorrId && x.Result == OperationResult.StreamDeleted));
	}
}
