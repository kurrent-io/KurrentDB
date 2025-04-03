// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Linq;
using System.Threading.Tasks;
using EventStore.ClientAPI;
using EventStore.Core.Tests.ClientAPI.Helpers;
using EventStore.Core.Tests.Helpers;
using NUnit.Framework;

namespace EventStore.Core.Tests.ClientAPI;

[Category("ClientAPI"), Category("LongRunning")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_having_max_count_set_for_stream<TLogFormat, TStreamId> : SpecificationWithDirectory {
	private const string Stream = "max-count-test-stream";

	private MiniNode<TLogFormat, TStreamId> _node;
	private IEventStoreConnection _connection;
	private EventData[] _testEvents;

	[SetUp]
	public override async Task SetUp() {
		await base.SetUp();
		_node = new MiniNode<TLogFormat, TStreamId>(PathName);
		await _node.Start();

		_connection = TestConnection.Create(_node.TcpEndPoint);
		await _connection.ConnectAsync();

		await _connection.SetStreamMetadataAsync(Stream, ExpectedVersion.NoStream,
			StreamMetadata.Build().SetMaxCount(3));

		_testEvents = Enumerable.Range(0, 5).Select(x => TestEvent.NewTestEvent(data: x.ToString())).ToArray();
		await _connection.AppendToStreamAsync(Stream, ExpectedVersion.NoStream, _testEvents);
	}

	[TearDown]
	public override async Task TearDown() {
		_connection.Close();
		await _node.Shutdown();
		await base.TearDown();
	}

	[Test]
	public async Task read_stream_forward_respects_max_count() {
		var res = await _connection.ReadStreamEventsForwardAsync(Stream, 0, 100, false);
		Assert.AreEqual(SliceReadStatus.Success, res.Status);
		Assert.AreEqual(3, res.Events.Length);
		Assert.AreEqual(_testEvents.Skip(2).Select(x => x.EventId).ToArray(),
			res.Events.Select(x => x.Event.EventId).ToArray());
	}

	[Test]
	public async Task read_stream_backward_respects_max_count() {
		var res = await _connection.ReadStreamEventsBackwardAsync(Stream, -1, 100, false);
		Assert.AreEqual(SliceReadStatus.Success, res.Status);
		Assert.AreEqual(3, res.Events.Length);
		Assert.AreEqual(_testEvents.Skip(2).Select(x => x.EventId).ToArray(),
			res.Events.Reverse().Select(x => x.Event.EventId).ToArray());
	}

	[Test]
	public async Task after_setting_less_strict_max_count_read_stream_forward_reads_more_events() {
		var res = await _connection.ReadStreamEventsForwardAsync(Stream, 0, 100, false);
		Assert.AreEqual(SliceReadStatus.Success, res.Status);
		Assert.AreEqual(3, res.Events.Length);
		Assert.AreEqual(_testEvents.Skip(2).Select(x => x.EventId).ToArray(),
			res.Events.Select(x => x.Event.EventId).ToArray());

		await _connection.SetStreamMetadataAsync(Stream, 0, StreamMetadata.Build().SetMaxCount(4));

		res = await _connection.ReadStreamEventsForwardAsync(Stream, 0, 100, false);
		Assert.AreEqual(SliceReadStatus.Success, res.Status);
		Assert.AreEqual(4, res.Events.Length);
		Assert.AreEqual(_testEvents.Skip(1).Select(x => x.EventId).ToArray(),
			res.Events.Select(x => x.Event.EventId).ToArray());
	}

	[Test]
	public async Task after_setting_more_strict_max_count_read_stream_forward_reads_less_events() {
		var res = await _connection.ReadStreamEventsForwardAsync(Stream, 0, 100, false);
		Assert.AreEqual(SliceReadStatus.Success, res.Status);
		Assert.AreEqual(3, res.Events.Length);
		Assert.AreEqual(_testEvents.Skip(2).Select(x => x.EventId).ToArray(),
			res.Events.Select(x => x.Event.EventId).ToArray());

		await _connection.SetStreamMetadataAsync(Stream, 0, StreamMetadata.Build().SetMaxCount(2));

		res = await _connection.ReadStreamEventsForwardAsync(Stream, 0, 100, false);
		Assert.AreEqual(SliceReadStatus.Success, res.Status);
		Assert.AreEqual(2, res.Events.Length);
		Assert.AreEqual(_testEvents.Skip(3).Select(x => x.EventId).ToArray(),
			res.Events.Select(x => x.Event.EventId).ToArray());
	}

	[Test]
	public async Task after_setting_less_strict_max_count_read_stream_backward_reads_more_events() {
		var res = await _connection.ReadStreamEventsBackwardAsync(Stream, -1, 100, false);
		Assert.AreEqual(SliceReadStatus.Success, res.Status);
		Assert.AreEqual(3, res.Events.Length);
		Assert.AreEqual(_testEvents.Skip(2).Select(x => x.EventId).ToArray(),
			res.Events.Reverse().Select(x => x.Event.EventId).ToArray());

		await _connection.SetStreamMetadataAsync(Stream, 0, StreamMetadata.Build().SetMaxCount(4));

		res = await _connection.ReadStreamEventsBackwardAsync(Stream, -1, 100, false);
		Assert.AreEqual(SliceReadStatus.Success, res.Status);
		Assert.AreEqual(4, res.Events.Length);
		Assert.AreEqual(_testEvents.Skip(1).Select(x => x.EventId).ToArray(),
			res.Events.Reverse().Select(x => x.Event.EventId).ToArray());
	}

	[Test]
	public async Task after_setting_more_strict_max_count_read_stream_backward_reads_less_events() {
		var res = await _connection.ReadStreamEventsBackwardAsync(Stream, -1, 100, false);
		Assert.AreEqual(SliceReadStatus.Success, res.Status);
		Assert.AreEqual(3, res.Events.Length);
		Assert.AreEqual(_testEvents.Skip(2).Select(x => x.EventId).ToArray(),
			res.Events.Reverse().Select(x => x.Event.EventId).ToArray());

		await _connection.SetStreamMetadataAsync(Stream, 0, StreamMetadata.Build().SetMaxCount(2));

		res = await _connection.ReadStreamEventsBackwardAsync(Stream, -1, 100, false);
		Assert.AreEqual(SliceReadStatus.Success, res.Status);
		Assert.AreEqual(2, res.Events.Length);
		Assert.AreEqual(_testEvents.Skip(3).Select(x => x.EventId).ToArray(),
			res.Events.Reverse().Select(x => x.Event.EventId).ToArray());
	}
}
