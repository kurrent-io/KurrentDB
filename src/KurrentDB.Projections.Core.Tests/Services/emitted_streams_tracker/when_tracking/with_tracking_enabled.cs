// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using EventStore.ClientAPI.SystemData;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Tests;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using KurrentDB.Projections.Core.Services.Processing.Emitting.EmittedEvents;
using NUnit.Framework;

namespace KurrentDB.Projections.Core.Tests.Services.emitted_streams_tracker.when_tracking;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class with_tracking_enabled<TLogFormat, TStreamId> : SpecificationWithEmittedStreamsTrackerAndDeleter<TLogFormat, TStreamId> {
	private readonly CountdownEvent _eventAppeared = new(1);
	private readonly UserCredentials _credentials = new("admin", "changeit");

	protected override async Task When() {
		var sub = await _conn.SubscribeToStreamAsync(_projectionNamesBuilder.GetEmittedStreamsName(), true, (_, _) => {
			_eventAppeared.Signal();
			return Task.CompletedTask;
		}, userCredentials: _credentials);

		_emittedStreamsTracker.TrackEmittedStream([
			new EmittedDataEvent("test_stream", Guid.NewGuid(), "type1", true, "data", null, CheckpointTag.FromPosition(0, 100, 50), null)
		]);

		_eventAppeared.Wait(TimeSpan.FromSeconds(5));
		sub.Unsubscribe();
	}

	[Test]
	public async Task should_write_a_stream_tracked_event() {
		var result = await _conn.ReadStreamEventsForwardAsync(_projectionNamesBuilder.GetEmittedStreamsName(), 0, 200, false, _credentials);
		Assert.AreEqual(1, result.Events.Length);
		Assert.AreEqual("test_stream", Helper.UTF8NoBom.GetString(result.Events[0].Event.Data));
		Assert.AreEqual(0, _eventAppeared.CurrentCount);
	}
}
