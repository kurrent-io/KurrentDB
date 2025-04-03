// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using NUnit.Framework;
using System;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Tests;

namespace EventStore.Projections.Core.Tests.Services.emitted_streams_deleter.when_deleting;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class with_no_emitted_streams_stream<TLogFormat, TStreamId> : SpecificationWithEmittedStreamsTrackerAndDeleter<TLogFormat, TStreamId> {
	protected Action _onDeleteStreamCompleted;
	protected ManualResetEvent _resetEvent = new ManualResetEvent(false);

	protected override Task Given() {
		_onDeleteStreamCompleted = () => { _resetEvent.Set(); };
		return base.Given();
	}

	protected override Task When() {
		_emittedStreamsDeleter.DeleteEmittedStreams(_onDeleteStreamCompleted);
		return Task.CompletedTask;
	}

	[Test]
	public void should_have_called_completed() {
		if (!_resetEvent.WaitOne(TimeSpan.FromSeconds(10))) {
			throw new Exception("Timed out waiting callback.");
		}

		;
	}
}
