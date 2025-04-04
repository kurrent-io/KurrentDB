// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using EventStore.Core.Helpers;
using NUnit.Framework;

namespace EventStore.Core.Tests.Helpers.IODispatcherTests.ReadEventsTests;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class async_read_stream_events_backward_with_cancelled_read<TLogFormat, TStreamId> : with_read_io_dispatcher<TLogFormat, TStreamId> {
	private bool _hasTimedOut;
	private bool _hasRead;
	private bool _eventSet;

	[OneTimeSetUp]
	public override void TestFixtureSetUp() {
		base.TestFixtureSetUp();
		var mre = new ManualResetEvent(false);
		var step = _ioDispatcher.BeginReadBackward(
			_cancellationScope, _eventStreamId, _fromEventNumber, _maxCount, true, _principal,
			res => {
				_hasRead = true;
				mre.Set();
			},
			() => {
				_hasTimedOut = true;
				mre.Set();
			}
		);

		IODispatcherAsync.Run(step);
		_cancellationScope.Cancel();
		_eventSet = mre.WaitOne(TimeSpan.FromSeconds(5));
	}

	[Test]
	public void should_ignore_read() {
		Assert.IsFalse(_eventSet);
		Assert.IsFalse(_hasRead, "Should not have completed read before replying on read message");
		_readBackward.Envelope.ReplyWith(CreateReadStreamEventsBackwardCompleted(_readBackward));
		Assert.IsFalse(_hasRead);
	}

	[Test]
	public void should_ignore_timeout_message() {
		Assert.IsFalse(_hasTimedOut, "Should not have timed out before replying on timeout message");
		_timeoutMessage.Reply();
		Assert.IsFalse(_hasTimedOut);
	}
}
