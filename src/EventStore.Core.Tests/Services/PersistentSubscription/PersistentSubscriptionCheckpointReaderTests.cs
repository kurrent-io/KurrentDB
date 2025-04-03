// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EventStore.Core.Bus;
using EventStore.Core.Helpers;
using EventStore.Core.Messaging;
using EventStore.Core.Services.PersistentSubscription;
using EventStore.Core.Tests.Helpers.IODispatcherTests;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.PersistentSubscription;

public class PersistentSubscriptionCheckpointReaderTests {
	[TestCase("SubscriptionCheckpoint")] // old checkpoints
	[TestCase("$SubscriptionCheckpoint")] // new checkpoints
	public void can_read_checkpoints(string checkpointEventType) {
		var bus = new SynchronousScheduler("persistent subscription test bus");

		bus.Subscribe(new AdHocHandler<Messages.ClientMessage.ReadStreamEventsBackward>(msg => {
			var lastEventNumber = msg.FromEventNumber + 1;
			var nextEventNumber = lastEventNumber + 1;

			var events = IODispatcherTestHelpers.CreateResolvedEvent<LogFormat.V2, string>(
				stream: msg.EventStreamId,
				eventType: checkpointEventType,
				data: "\"the checkpoint data\"");

			msg.Envelope.ReplyWith(new Messages.ClientMessage.ReadStreamEventsBackwardCompleted(
				correlationId: msg.CorrelationId,
				eventStreamId: msg.EventStreamId,
				fromEventNumber: msg.FromEventNumber,
				maxCount: msg.MaxCount,
				result: Data.ReadStreamResult.Success,
				events: events,
				streamMetadata: null,
				isCachePublic: false,
				error: "",
				nextEventNumber: nextEventNumber,
				lastEventNumber: lastEventNumber,
				isEndOfStream: false,
				tfLastCommitPosition: 0));
		}));

		var ioDispatcher = new IODispatcher(bus, bus);
		IODispatcherTestHelpers.SubscribeIODispatcher(ioDispatcher, bus);
		var sut = new PersistentSubscriptionCheckpointReader(ioDispatcher);

		var loadedState = "";
		sut.BeginLoadState("subscriptionA", state => {
			loadedState = state;
		});

		AssertEx.IsOrBecomesTrue(() => loadedState == "the checkpoint data");
	}
}
