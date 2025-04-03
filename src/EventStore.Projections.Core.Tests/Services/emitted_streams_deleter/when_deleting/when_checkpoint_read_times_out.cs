// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using EventStore.Core.Bus;
using EventStore.Core.Data;
using EventStore.Core.Helpers;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Services.TimerService;
using EventStore.Core.Tests;
using EventStore.Core.TransactionLog.LogRecords;
using EventStore.Projections.Core.Services;
using EventStore.Projections.Core.Services.Processing;
using NUnit.Framework;
using ResolvedEvent = EventStore.Core.Data.ResolvedEvent;

namespace EventStore.Projections.Core.Tests.Services.emitted_streams_deleter.when_deleting;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_checkpoint_read_times_out<TLogFormat, TStreamId> : with_emitted_stream_deleter<TLogFormat, TStreamId>,
	IHandle<TimerMessage.Schedule> {
	protected Action _onDeleteStreamCompleted;
	private ManualResetEventSlim _mre = new ManualResetEventSlim();
	private List<ClientMessage.DeleteStream> _deleteMessages = new List<ClientMessage.DeleteStream>();
	private bool _hasTimerTimedOut;

	private Guid _timedOutCorrelationId;

	public override void When() {
		_bus.Subscribe<TimerMessage.Schedule>(this);
		_onDeleteStreamCompleted = () => { _mre.Set(); };

		_deleter.DeleteEmittedStreams(_onDeleteStreamCompleted);
	}

	public override void Handle(ClientMessage.ReadStreamEventsBackward message) {
		if (message.CorrelationId == _timedOutCorrelationId) {
			return;
		} else {
			base.Handle(message);
		}
	}

	public override void Handle(ClientMessage.DeleteStream message) {
		_deleteMessages.Add(message);
		message.Envelope.ReplyWith(new ClientMessage.DeleteStreamCompleted(
			message.CorrelationId, OperationResult.Success, String.Empty));
	}

	public void Handle(TimerMessage.Schedule message) {
		if (!_hasTimerTimedOut) {
			var delay = message.ReplyMessage as IODispatcherDelayedMessage;
			if (delay != null) {
				_timedOutCorrelationId = delay.MessageCorrelationId.Value;
				_hasTimerTimedOut = true;
				message.Reply();
			}
		}
	}

	[Test]
	public void should_have_deleted_the_tracked_emitted_stream() {
		if (!_mre.Wait(10000)) {
			Assert.Fail("Timed out waiting for event to be deleted");
		}

		Assert.AreEqual(_testStreamName, _deleteMessages[0].EventStreamId);
		Assert.AreEqual(_checkpointName, _deleteMessages[1].EventStreamId);
	}
}
