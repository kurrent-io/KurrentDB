// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Core.Tests;
using NUnit.Framework;

namespace KurrentDB.Projections.Core.Tests.Services.emitted_streams_deleter.when_deleting;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class with_no_emitted_streams_stream<TLogFormat, TStreamId> : SpecificationWithEmittedStreamsTrackerAndDeleter<TLogFormat, TStreamId> {
	private Action _onDeleteStreamCompleted;
	private readonly ManualResetEvent _resetEvent = new(false);

	protected override Task Given() {
		_onDeleteStreamCompleted = () => _resetEvent.Set();
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
	}
}
