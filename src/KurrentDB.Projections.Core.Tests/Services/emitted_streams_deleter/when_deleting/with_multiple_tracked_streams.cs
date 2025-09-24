// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using EventStore.ClientAPI;
using EventStore.ClientAPI.SystemData;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Tests;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using KurrentDB.Projections.Core.Services.Processing.Emitting.EmittedEvents;
using NUnit.Framework;

namespace KurrentDB.Projections.Core.Tests.Services.emitted_streams_deleter.when_deleting;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class with_multiple_tracked_streams<TLogFormat, TStreamId> : SpecificationWithEmittedStreamsTrackerAndDeleter<TLogFormat, TStreamId> {
	private Action _onDeleteStreamCompleted;
	private ManualResetEvent _resetEvent = new(false);
	private CountdownEvent _eventAppeared;
	private int _numberOfTrackedEvents = 200;
	private const string TestStreamFormat = "test_stream_{0}";
	private UserCredentials _credentials;

	protected override async Task Given() {
		_credentials = new UserCredentials("admin", "changeit");
		_eventAppeared = new CountdownEvent(_numberOfTrackedEvents);
		_onDeleteStreamCompleted = () => _resetEvent.Set();
		await base.Given();

		var sub = await _conn.SubscribeToStreamAsync(_projectionNamesBuilder.GetEmittedStreamsName(), true, (_, _) => {
			_eventAppeared.Signal();
			return Task.CompletedTask;
		}, userCredentials: _credentials);

		for (int i = 0; i < _numberOfTrackedEvents; i++) {
			await _conn.AppendToStreamAsync(string.Format(TestStreamFormat, i), ExpectedVersion.Any,
				new EventData(Guid.NewGuid(), "type1", true, Helper.UTF8NoBom.GetBytes("data"), null));
			_emittedStreamsTracker.TrackEmittedStream([
				new EmittedDataEvent(string.Format(TestStreamFormat, i), Guid.NewGuid(), "type1", true, "data", null, CheckpointTag.FromPosition(0, 100, 50),
					null)
			]);
		}

		if (!_eventAppeared.Wait(TimeSpan.FromSeconds(10))) {
			Assert.Fail("Timed out waiting for emitted streams");
		}

		var emittedStreamResult =
			await _conn.ReadStreamEventsForwardAsync(_projectionNamesBuilder.GetEmittedStreamsName(), 0, _numberOfTrackedEvents, false, _credentials);
		Assert.AreEqual(_numberOfTrackedEvents, emittedStreamResult.Events.Length);
		Assert.AreEqual(SliceReadStatus.Success, emittedStreamResult.Status);
	}

	protected override Task When() {
		_emittedStreamsDeleter.DeleteEmittedStreams(_onDeleteStreamCompleted);
		return !_resetEvent.WaitOne(TimeSpan.FromSeconds(10)) ? throw new Exception("Timed out waiting callback.") : Task.CompletedTask;
	}

	[Test]
	public async Task should_have_deleted_the_tracked_emitted_streams() {
		for (int i = 0; i < _numberOfTrackedEvents; i++) {
			var result = await _conn.ReadStreamEventsForwardAsync(string.Format(TestStreamFormat, i), 0, 1, false, new("admin", "changeit"));
			Assert.AreEqual(SliceReadStatus.StreamNotFound, result.Status);
		}
	}


	[Test]
	public async Task should_have_deleted_the_checkpoint_stream() {
		var result = await _conn.ReadStreamEventsForwardAsync(_projectionNamesBuilder.GetEmittedStreamsCheckpointName(), 0, 1, false, new("admin", "changeit"));
		Assert.AreEqual(SliceReadStatus.StreamNotFound, result.Status);
	}

	[Test]
	public async Task should_have_deleted_the_emitted_streams_stream() {
		var result = await _conn.ReadStreamEventsForwardAsync(_projectionNamesBuilder.GetEmittedStreamsName(), 0, 1, false, new("admin", "changeit"));
		Assert.AreEqual(SliceReadStatus.StreamNotFound, result.Status);
	}
}
