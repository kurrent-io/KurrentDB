// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Tests.Helpers;
using NUnit.Framework;
using WriteEventsMgr = EventStore.Core.Services.RequestManager.Managers.WriteEvents;

namespace EventStore.Core.Tests.Services.RequestManagement.Service;

[TestFixture]
public class when_handling_write_stream : RequestManagerServiceSpecification {
	protected override void Given() {
		Dispatcher.Publish(new ClientMessage.WriteEvents(InternalCorrId, ClientCorrId, Envelope, true, StreamId, ExpectedVersion.Any, new[] { DummyEvent() }, null));
		Dispatcher.Publish(new StorageMessage.CommitIndexed(InternalCorrId, LogPosition, 2, 3, 3));
		Dispatcher.Publish(new ReplicationTrackingMessage.ReplicatedTo(LogPosition));
	}

	protected override Message When() {
		return new ReplicationTrackingMessage.IndexedTo(LogPosition);
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
