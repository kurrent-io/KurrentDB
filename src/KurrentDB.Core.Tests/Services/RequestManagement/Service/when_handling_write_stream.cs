// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Tests.Helpers;
using NUnit.Framework;

namespace KurrentDB.Core.Tests.Services.RequestManagement.Service;

[TestFixture]
public class when_handling_write_stream : RequestManagerServiceSpecification {
	protected override void Given() {
		var events = new Event[] { DummyEvent(), DummyEvent(), DummyEvent() };
		Dispatcher.Publish(ClientMessage.WriteEvents.ForSingleStream(InternalCorrId, ClientCorrId, Envelope, true, StreamId, ExpectedVersion.Any, new(events), null));
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
		Assert.That(Envelope.Replies.ContainsSingle<ClientMessage.WriteEventsCompleted>(x =>
			x.CorrelationId == ClientCorrId &&
			x.Result == OperationResult.Success &&
			x.FirstEventNumbers.Single == 0 &&
			x.LastEventNumbers.Single == 2));
	}
}
