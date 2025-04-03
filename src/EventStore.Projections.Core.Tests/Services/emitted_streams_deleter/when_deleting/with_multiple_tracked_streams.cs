// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.ClientAPI;
using EventStore.Common.Utils;
using EventStore.Projections.Core.Services.Processing;
using NUnit.Framework;
using System;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Tests;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using EventStore.Projections.Core.Services.Processing.Emitting;
using EventStore.Projections.Core.Services.Processing.Emitting.EmittedEvents;

namespace EventStore.Projections.Core.Tests.Services.emitted_streams_deleter.when_deleting;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class with_multiple_tracked_streams<TLogFormat, TStreamId> : SpecificationWithEmittedStreamsTrackerAndDeleter<TLogFormat, TStreamId> {
	protected Action _onDeleteStreamCompleted;
	protected ManualResetEvent _resetEvent = new ManualResetEvent(false);
	protected CountdownEvent _eventAppeared;
	private int _numberOfTrackedEvents = 200;
	private string _testStreamFormat = "test_stream_{0}";
	private EventStore.ClientAPI.SystemData.UserCredentials _credentials;

	protected override async Task Given() {
		_credentials = new EventStore.ClientAPI.SystemData.UserCredentials("admin", "changeit");
		_eventAppeared = new CountdownEvent(_numberOfTrackedEvents);
		_onDeleteStreamCompleted = () => { _resetEvent.Set(); };
		await base.Given();

		var sub = await _conn.SubscribeToStreamAsync(_projectionNamesBuilder.GetEmittedStreamsName(), true, (s, evnt) => {
			_eventAppeared.Signal();
			return Task.CompletedTask;
		}, userCredentials: _credentials);

		for (int i = 0; i < _numberOfTrackedEvents; i++) {
			await _conn.AppendToStreamAsync(String.Format(_testStreamFormat, i), ExpectedVersion.Any,
				new EventData(Guid.NewGuid(), "type1", true, Helper.UTF8NoBom.GetBytes("data"), null));
			_emittedStreamsTracker.TrackEmittedStream(new EmittedEvent[] {
				new EmittedDataEvent(
					String.Format(_testStreamFormat, i), Guid.NewGuid(), "type1", true,
					"data", null, CheckpointTag.FromPosition(0, 100, 50), null),
			});
		}

		if (!_eventAppeared.Wait(TimeSpan.FromSeconds(10))) {
			Assert.Fail("Timed out waiting for emitted streams");
		}

		var emittedStreamResult =
			await _conn.ReadStreamEventsForwardAsync(_projectionNamesBuilder.GetEmittedStreamsName(), 0,
				_numberOfTrackedEvents, false, _credentials);
		Assert.AreEqual(_numberOfTrackedEvents, emittedStreamResult.Events.Length);
		Assert.AreEqual(SliceReadStatus.Success, emittedStreamResult.Status);
	}

	protected override Task When() {
		_emittedStreamsDeleter.DeleteEmittedStreams(_onDeleteStreamCompleted);
		if (!_resetEvent.WaitOne(TimeSpan.FromSeconds(10))) {
			throw new Exception("Timed out waiting callback.");
		}

		return Task.CompletedTask;
	}

	[Test]
	public async Task should_have_deleted_the_tracked_emitted_streams() {
		for (int i = 0; i < _numberOfTrackedEvents; i++) {
			var result = await _conn.ReadStreamEventsForwardAsync(String.Format(_testStreamFormat, i), 0, 1, false,
				new EventStore.ClientAPI.SystemData.UserCredentials("admin", "changeit"));
			Assert.AreEqual(SliceReadStatus.StreamNotFound, result.Status);
		}
	}


	[Test]
	public async Task should_have_deleted_the_checkpoint_stream() {
		var result = await _conn.ReadStreamEventsForwardAsync(_projectionNamesBuilder.GetEmittedStreamsCheckpointName(),
			0, 1, false, new EventStore.ClientAPI.SystemData.UserCredentials("admin", "changeit"));
		Assert.AreEqual(SliceReadStatus.StreamNotFound, result.Status);
	}

	[Test]
	public async Task should_have_deleted_the_emitted_streams_stream() {
		var result = await _conn.ReadStreamEventsForwardAsync(_projectionNamesBuilder.GetEmittedStreamsName(), 0, 1,
			false, new EventStore.ClientAPI.SystemData.UserCredentials("admin", "changeit"));
		Assert.AreEqual(SliceReadStatus.StreamNotFound, result.Status);
	}
}
