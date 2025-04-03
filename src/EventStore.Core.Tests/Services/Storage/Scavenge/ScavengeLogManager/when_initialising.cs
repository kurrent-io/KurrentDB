// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EventStore.Common.Utils;
using EventStore.Core.Bus;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Services;
using EventStore.Core.Tests.Helpers;
using EventStore.Core.TransactionLog.Chunks;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.Storage.Scavenge.ScavengeLogManager;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_scavenges_stream_does_not_exist<TLogFormat, TStreamId> : TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
	private TFChunkScavengerLogManager _logManager;
	private TaskCompletionSource<ClientMessage.WriteEvents> eventWritten = new();
	private StreamMetadata _metadata;

	protected override void Given1() {
		NoOtherStreams();
		AllWritesSucceed();

		var scavengeHistoryMaxAge = TimeSpan.FromMinutes(5);
		_metadata = ScavengerLogHelper.CreateScavengeMetadata(scavengeHistoryMaxAge);
		_bus.Subscribe(new AdHocHandler<ClientMessage.WriteEvents>(m => eventWritten.TrySetResult(m)));

		_logManager = new TFChunkScavengerLogManager("localhost:2113", scavengeHistoryMaxAge, _ioDispatcher);
		_logManager.Initialise();
	}

	[Test]
	public async Task should_write_scavenge_stream_metadata() {
		var evnt = await eventWritten.Task.WithTimeout();
		Assert.AreEqual(SystemStreams.MetastreamOf(SystemStreams.ScavengesStream), evnt.EventStreamId);
		Assert.AreEqual(_metadata.ToJsonBytes(), evnt.Events[0].Data);
	}
}

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_scavenges_stream_has_different_metadata<TLogFormat, TStreamId> : TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
	private TFChunkScavengerLogManager _logManager;
	private TaskCompletionSource<ClientMessage.WriteEvents> eventWritten = new();
	private TimeSpan _scavengeHistoryMaxAge = TimeSpan.FromMinutes(5);

	protected override void Given1() {
		NoOtherStreams();
		AllWritesSucceed();

		_bus.Subscribe(new AdHocHandler<ClientMessage.WriteEvents>(m => eventWritten.TrySetResult(m)));

		// Create the existing metadata event with a max age of 30 days
		var metadata = ScavengerLogHelper.CreateScavengeMetadata(TimeSpan.FromDays(30));
		ExistingStreamMetadata(SystemStreams.MetastreamOf(SystemStreams.ScavengesStream), metadata.ToJsonString());

		_logManager = new TFChunkScavengerLogManager("localhost:2113", _scavengeHistoryMaxAge, _ioDispatcher);
		_logManager.Initialise();
	}

	[Test]
	public async Task should_write_new_scavenge_stream_metadata() {
		var evnt = await eventWritten.Task.WithTimeout();
		Assert.AreEqual(SystemStreams.MetastreamOf(SystemStreams.ScavengesStream), evnt.EventStreamId);
		var expectedMetadata = ScavengerLogHelper.CreateScavengeMetadata(_scavengeHistoryMaxAge);
		Assert.AreEqual(expectedMetadata.ToJsonBytes(), evnt.Events[0].Data);
	}
}

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_scavenges_stream_has_correct_metadata<TLogFormat, TStreamId> : TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
	private TFChunkScavengerLogManager _logManager;
	private TaskCompletionSource<ClientMessage.ReadStreamEventsBackward> _eventRead = new();
	private List<ClientMessage.WriteEvents> _writeRequests = new();
	private TimeSpan _scavengeHistoryMaxAge = TimeSpan.FromMinutes(5);

	protected override void Given1() {
		NoOtherStreams();
		AllWritesSucceed();

		_bus.Subscribe(new AdHocHandler<ClientMessage.WriteEvents>(m => _writeRequests.Add(m)));
		_bus.Subscribe(new AdHocHandler<ClientMessage.ReadStreamEventsBackward>(m => _eventRead.TrySetResult(m)));

		// Create the existing metadata event with the correct max age
		var metadata = ScavengerLogHelper.CreateScavengeMetadata(_scavengeHistoryMaxAge);
		ExistingStreamMetadata(SystemStreams.ScavengesStream, metadata.ToJsonString());

		_logManager = new TFChunkScavengerLogManager("localhost:2113", _scavengeHistoryMaxAge, _ioDispatcher);
		_logManager.Initialise();
	}

	[Test]
	public async Task should_not_write_new_metadata() {
		var evnt = await _eventRead.Task.WithTimeout();
		await Task.Delay(100); // Give it time to finish any writes if there are any
		Assert.AreEqual(SystemStreams.MetastreamOf(SystemStreams.ScavengesStream), evnt.EventStreamId);
		Assert.IsEmpty(_writeRequests);
	}
}

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_previous_scavenge_was_interrupted_but_scavenge_stream_not_written<TLogFormat, TStreamId>
	: TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
	private const string _nodeEndpoint = "localhost:2113";
	private Guid _scavengeId = Guid.NewGuid();
	private TFChunkScavengerLogManager _logManager;
	private TaskCompletionSource<ClientMessage.WriteEvents> _eventWritten = new();
	private string _scavengeStreamId;

	protected override void Given1() {
		NoOtherStreams();
		AllWritesSucceed();
		_scavengeStreamId = ScavengerLogHelper.ScavengeStreamId(_scavengeId);

		_bus.Subscribe(new AdHocHandler<ClientMessage.WriteEvents>(m => {
			if (m.EventStreamId == _scavengeStreamId
			    && m.Events[0].EventType == SystemEventTypes.ScavengeCompleted) {
				_eventWritten.SetResult(m);
			}
		}));

		// Create the existing scavenge
		var scavengeHistoryMaxAge = TimeSpan.FromMinutes(5);
		ExistingStreamMetadata(SystemStreams.ScavengesStream,
			ScavengerLogHelper.CreateScavengeMetadata(scavengeHistoryMaxAge).ToJsonString());
		var startedData = ScavengerLogHelper.CreateScavengeStarted(_scavengeId, _nodeEndpoint);
		// This should be a linkTo event pointing to the scavenge stream
		ExistingEvent(SystemStreams.ScavengesStream, SystemEventTypes.ScavengeStarted, "", startedData.ToJson(), true);

		_logManager = new TFChunkScavengerLogManager(_nodeEndpoint, scavengeHistoryMaxAge, _ioDispatcher);
		_logManager.Initialise();
	}

	[Test]
	public async Task should_complete_the_scavenge_as_faulted() {
		var evnt = await _eventWritten.Task.WithTimeout();
		var expectedData = ScavengerLogHelper.CreateScavengeInterruptedByRestart(_scavengeId, _nodeEndpoint, TimeSpan.Zero);
		Assert.AreEqual(expectedData.ToJson(), Encoding.UTF8.GetString(evnt.Events[0].Data));
	}
}

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_previous_scavenge_was_interrupted_and_some_data_was_scavenged<TLogFormat, TStreamId>
	: TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
	private const string _nodeEndpoint = "localhost:2113";
	private Guid _scavengeId = Guid.NewGuid();
	private TFChunkScavengerLogManager _logManager;
	private TaskCompletionSource<ClientMessage.WriteEvents> _eventWritten = new();
	private string _scavengeStreamId;

	protected override void Given1() {
		NoOtherStreams();
		AllWritesSucceed();
		_scavengeStreamId = ScavengerLogHelper.ScavengeStreamId(_scavengeId);

		_bus.Subscribe(new AdHocHandler<ClientMessage.WriteEvents>(m => {
			if (m.EventStreamId == _scavengeStreamId
			    && m.Events[0].EventType == SystemEventTypes.ScavengeCompleted) {
				_eventWritten.SetResult(m);
			}
		}));

		// Create the existing scavenge
		var scavengeHistoryMaxAge = TimeSpan.FromMinutes(5);
		var metadata = ScavengerLogHelper.CreateScavengeMetadata(scavengeHistoryMaxAge);
		ExistingStreamMetadata(SystemStreams.ScavengesStream, metadata.ToJsonString());
		var startedData = ScavengerLogHelper.CreateScavengeStarted(_scavengeId, _nodeEndpoint);
		// This should be a linkTo event pointing to the scavenge stream
		ExistingEvent(SystemStreams.ScavengesStream, SystemEventTypes.ScavengeStarted, "", startedData.ToJson(), true);

		var firstChunk = ScavengerLogHelper.CreateScavengeChunkCompleted(
			_scavengeId, _nodeEndpoint, 0, 0, TimeSpan.FromSeconds(1), int.MaxValue).ToJson();
		ExistingEvent(_scavengeStreamId, SystemEventTypes.ScavengeChunksCompleted, "", firstChunk, true);

		var secondChunk = ScavengerLogHelper.CreateScavengeChunkCompleted(
			_scavengeId, _nodeEndpoint, 1, 1, TimeSpan.FromSeconds(1), int.MaxValue).ToJson();
		ExistingEvent(_scavengeStreamId, SystemEventTypes.ScavengeChunksCompleted, "", secondChunk, true);

		_logManager = new TFChunkScavengerLogManager(_nodeEndpoint, scavengeHistoryMaxAge, _ioDispatcher);
		_logManager.Initialise();
	}

	[Test]
	public async Task should_complete_the_scavenge_as_faulted() {
		var evnt = await _eventWritten.Task.WithTimeout();
		long spaceSavedPerChunk = int.MaxValue;
		var expectedData = ScavengerLogHelper.CreateScavengeInterruptedByRestart(
			_scavengeId, _nodeEndpoint, TimeSpan.FromSeconds(2), spaceSavedPerChunk * 2, 1);
		Assert.AreEqual(expectedData.ToJson(), Encoding.UTF8.GetString(evnt.Events[0].Data));
	}
}

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_previous_scavenge_was_completed<TLogFormat, TStreamId>
	: TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
	private const string _nodeEndpoint = "localhost:2113";
	private Guid _scavengeId = Guid.NewGuid();
	private TFChunkScavengerLogManager _logManager;
	private TaskCompletionSource<ClientMessage.ReadStreamEventsBackward> _eventRead = new();
	private List<ClientMessage.WriteEvents> _writtenEvents = new();
	private string _scavengeStreamId;

	protected override void Given1() {
		NoOtherStreams();
		AllWritesSucceed();
		_scavengeStreamId = ScavengerLogHelper.ScavengeStreamId(_scavengeId);

		_bus.Subscribe(new AdHocHandler<ClientMessage.WriteEvents>(m => _writtenEvents.Add(m)));
		_bus.Subscribe(new AdHocHandler<ClientMessage.ReadStreamEventsBackward>(m => {
			if (m.EventStreamId == SystemStreams.ScavengesStream) {
				_eventRead.SetResult(m);
			}
		}));

		// Create the existing scavenge
		var scavengeHistoryMaxAge = TimeSpan.FromMinutes(5);
		var metadata = ScavengerLogHelper.CreateScavengeMetadata(scavengeHistoryMaxAge);
		ExistingStreamMetadata(SystemStreams.ScavengesStream, metadata.ToJsonString());
		var startedData = ScavengerLogHelper.CreateScavengeStarted(_scavengeId, _nodeEndpoint);
		// This should be a linkTo event pointing to the scavenge stream
		ExistingEvent(SystemStreams.ScavengesStream, SystemEventTypes.ScavengeStarted, "", startedData.ToJson(), true);

		var firstChunk = ScavengerLogHelper.CreateScavengeChunkCompleted(
			_scavengeId, _nodeEndpoint, 0, 0, TimeSpan.FromSeconds(1), 10).ToJson();
		ExistingEvent(_scavengeStreamId, SystemEventTypes.ScavengeChunksCompleted, "", firstChunk, true);

		var secondChunk = ScavengerLogHelper.CreateScavengeChunkCompleted(
			_scavengeId, _nodeEndpoint, 1, 1, TimeSpan.FromSeconds(1), 10).ToJson();
		ExistingEvent(_scavengeStreamId, SystemEventTypes.ScavengeChunksCompleted, "", secondChunk, true);

		var scavengeCompleted = ScavengerLogHelper.CreateScavengeCompletedSuccessfully(
			_scavengeId, _nodeEndpoint, TimeSpan.FromSeconds(2), 20, 1).ToJson();
		// This should be a linkTo event pointing to the scavenge stream
		ExistingEvent(SystemStreams.ScavengesStream, SystemEventTypes.ScavengeCompleted, "", scavengeCompleted, true);

		_logManager = new TFChunkScavengerLogManager(_nodeEndpoint, scavengeHistoryMaxAge, _ioDispatcher);
		_logManager.Initialise();
	}

	[Test]
	public async Task should_not_write_any_new_events() {
		await _eventRead.Task.WithTimeout();
		await Task.Delay(100); // Give it some time to write any events
		Assert.IsEmpty(_writtenEvents.Where(x => x.EventStreamId == _scavengeStreamId));
	}
}
