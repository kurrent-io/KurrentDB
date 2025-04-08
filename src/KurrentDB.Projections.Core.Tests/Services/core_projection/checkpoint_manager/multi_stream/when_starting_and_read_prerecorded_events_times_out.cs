// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Threading;
using EventStore.Core.Messages;
using EventStore.Core.Tests;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Services.TimerService;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using NUnit.Framework;
using IODispatcherDelayedMessage = KurrentDB.Core.Helpers.IODispatcherDelayedMessage;

namespace KurrentDB.Projections.Core.Tests.Services.core_projection.checkpoint_manager.multi_stream;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_starting_and_read_prerecorded_events_times_out<TLogFormat, TStreamId> : with_multi_stream_checkpoint_manager<TLogFormat, TStreamId>,
	IHandle<TimerMessage.Schedule>,
	IHandle<CoreProjectionProcessingMessage.PrerecordedEventsLoaded> {
	private bool _hasTimedOut;
	private Guid _timeoutCorrelationId;
	private ManualResetEventSlim _mre = new ManualResetEventSlim();
	private CoreProjectionProcessingMessage.PrerecordedEventsLoaded _eventsLoadedMessage;

	public override void When() {
		_bus.Subscribe<TimerMessage.Schedule>(this);
		_bus.Subscribe<CoreProjectionProcessingMessage.PrerecordedEventsLoaded>(this);

		_checkpointManager.Initialize();
		var positions = new Dictionary<string, long> { { "a", 1 }, { "b", 1 }, { "c", 1 } };
		_checkpointManager.BeginLoadPrerecordedEvents(CheckpointTag.FromStreamPositions(0, positions));

		if (!_mre.Wait(10000)) {
			Assert.Fail("Timed out waiting for pre recorded events loaded message");
		}
	}

	public override void Handle(ClientMessage.ReadStreamEventsBackward message) {
		if (!_hasTimedOut) {
			_hasTimedOut = true;
			_timeoutCorrelationId = message.CorrelationId;
			return;
		}

		base.Handle(message);
	}

	public void Handle(TimerMessage.Schedule message) {
		var delay = message.ReplyMessage as IODispatcherDelayedMessage;
		if (delay != null && delay.MessageCorrelationId == _timeoutCorrelationId) {
			message.Reply();
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
