// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Services.TimerService;
using KurrentDB.Core.Tests;
using KurrentDB.Core.Tests.Helpers.IODispatcherTests;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services;
using NUnit.Framework;
using IODispatcherDelayedMessage = KurrentDB.Core.Helpers.IODispatcherDelayedMessage;

namespace KurrentDB.Projections.Core.Tests.Services.core_projection.projection_checkpoint_reader;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_projection_reader_times_out_on_read<TLogFormat, TStreamId> : with_projection_checkpoint_reader<TLogFormat, TStreamId>,
	IHandle<CoreProjectionProcessingMessage.CheckpointLoaded>,
	IHandle<TimerMessage.Schedule> {
	private readonly ManualResetEventSlim _mre = new();
	private CoreProjectionProcessingMessage.CheckpointLoaded _checkpointLoaded;
	private bool _hasTimedOut;
	private Guid _timeoutCorrelationId;

	protected override void When() {
		_bus.Subscribe<CoreProjectionProcessingMessage.CheckpointLoaded>(this);
		_bus.Subscribe<TimerMessage.Schedule>(this);

		_reader.Initialize();
		_reader.BeginLoadState();
		if (!_mre.Wait(10000)) {
			Assert.Fail("Timed out waiting for checkpoint to load");
		}
	}

	public override void Handle(ClientMessage.ReadStreamEventsBackward message) {
		if (!_hasTimedOut) {
			_timeoutCorrelationId = message.CorrelationId;
			_hasTimedOut = true;
			return;
		}

		var evnts = IODispatcherTestHelpers.CreateResolvedEvent<TLogFormat, TStreamId>(message.EventStreamId,
			ProjectionEventTypes.ProjectionCheckpoint, "[]",
			"""
			{
			  "$v": "1:-1:3:3",
			  "$c": 269728,
			  "$p": 269728
			}
			""");
		var reply = new ClientMessage.ReadStreamEventsBackwardCompleted(message.CorrelationId,
			message.EventStreamId, message.FromEventNumber, message.MaxCount, ReadStreamResult.Success,
			evnts, null, true, "", 0, 0, true, 10000);
		message.Envelope.ReplyWith(reply);
	}

	public void Handle(TimerMessage.Schedule message) {
		var delay = message.ReplyMessage as IODispatcherDelayedMessage;
		if (delay != null && delay.MessageCorrelationId == _timeoutCorrelationId) {
			message.Reply();
		}
	}

	public void Handle(CoreProjectionProcessingMessage.CheckpointLoaded message) {
		_checkpointLoaded = message;
		_mre.Set();
	}

	[Test]
	public void should_load_checkpoint() {
		Assert.IsNotNull(_checkpointLoaded);
		Assert.AreEqual(_checkpointLoaded.ProjectionId, _projectionId);
	}
}
