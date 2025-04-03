// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.Threading;
using EventStore.Core.Bus;
using EventStore.Core.Tests;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using NUnit.Framework;
using ResolvedEvent = EventStore.Core.Data.ResolvedEvent;

namespace EventStore.Projections.Core.Tests.Services.core_projection.checkpoint_manager.multi_stream;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_starting_and_read_prerecorded_events_successfully<TLogFormat, TStreamId> : with_multi_stream_checkpoint_manager<TLogFormat, TStreamId>,
	IHandle<CoreProjectionProcessingMessage.PrerecordedEventsLoaded> {
	private ManualResetEventSlim _mre = new ManualResetEventSlim();
	private CoreProjectionProcessingMessage.PrerecordedEventsLoaded _eventsLoadedMessage;

	public override void When() {
		_bus.Subscribe<CoreProjectionProcessingMessage.PrerecordedEventsLoaded>(this);

		_checkpointManager.Initialize();
		var positions = new Dictionary<string, long> {{"a", 1}, {"b", 1}, {"c", 1}};
		_checkpointManager.BeginLoadPrerecordedEvents(CheckpointTag.FromStreamPositions(0, positions));

		if (!_mre.Wait(10000)) {
			Assert.Fail("Timed out waiting for pre recorded events loaded message");
		}
	}

	public void Handle(CoreProjectionProcessingMessage.PrerecordedEventsLoaded message) {
		_eventsLoadedMessage = message;
		_mre.Set();
	}

	[Test]
	public void should_send_prerecorded_events_message() {
		Assert.IsNotNull(_eventsLoadedMessage);
	}
}
