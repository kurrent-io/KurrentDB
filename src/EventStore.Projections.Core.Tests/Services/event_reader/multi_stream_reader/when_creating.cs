// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using EventStore.Core.Services.TimerService;
using EventStore.Core.Tests;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.MultiStream;
using EventStore.Projections.Core.Tests.Services.core_projection;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.event_reader.multi_stream_reader;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_creating<TLogFormat, TStreamId> : TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
	private string[] _abStreams;
	private Dictionary<string, long> _ab12Tag;
	private new RealTimeProvider _timeProvider;

	[SetUp]
	public void setup() {
		_timeProvider = new RealTimeProvider();
		_ab12Tag = new Dictionary<string, long> {{"a", 1}, {"b", 2}};
		_abStreams = new[] {"a", "b"};
	}

	[Test]
	public void it_can_be_created() {
		new MultiStreamEventReader(
			_ioDispatcher, _bus, Guid.NewGuid(), null, 0, _abStreams, _ab12Tag, false, _timeProvider);
	}

	[Test]
	public void null_publisher_throws_argument_null_exception() {
		Assert.Throws<ArgumentNullException>(() => {
			new MultiStreamEventReader(
				_ioDispatcher, null, Guid.NewGuid(), null, 0, _abStreams, _ab12Tag, false, _timeProvider);
		});
	}

	[Test]
	public void empty_event_reader_id_throws_argument_exception() {
		Assert.Throws<ArgumentException>(() => {
			new MultiStreamEventReader(
				_ioDispatcher, _bus, Guid.Empty, null, 0, _abStreams, _ab12Tag, false, _timeProvider);
		});
	}

	[Test]
	public void null_streams_throws_argument_null_exception() {
		Assert.Throws<ArgumentNullException>(() => {
			new MultiStreamEventReader(
				_ioDispatcher, _bus, Guid.NewGuid(), null, 0, null, _ab12Tag, false, _timeProvider);
		});
	}

	[Test]
	public void null_time_provider_throws_argument_null_exception() {
		Assert.Throws<ArgumentNullException>(() => {
			new MultiStreamEventReader(_ioDispatcher, _bus, Guid.NewGuid(), null, 0, _abStreams, _ab12Tag, false,
				null);
		});
	}

	[Test]
	public void empty_streams_throws_argument_exception() {
		Assert.Throws<ArgumentException>(() => {
			new MultiStreamEventReader(
				_ioDispatcher, _bus, Guid.NewGuid(), null, 0, new string[0], _ab12Tag, false, _timeProvider);
		});
	}

	[Test]
	public void invalid_from_tag_throws_argument_exception() {
		Assert.Throws<ArgumentException>(() => {
			new MultiStreamEventReader(
				_ioDispatcher, _bus, Guid.NewGuid(), null, 0, _abStreams,
				new Dictionary<string, long> {{"a", 1}, {"c", 2}}, false, _timeProvider);
		});
	}
}
