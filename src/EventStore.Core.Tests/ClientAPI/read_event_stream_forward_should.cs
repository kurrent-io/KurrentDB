// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Globalization;
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
public class read_event_stream_forward_should<TLogFormat, TStreamId> : SpecificationWithDirectoryPerTestFixture {
	private MiniNode<TLogFormat, TStreamId> _node;

	[OneTimeSetUp]
	public override async Task TestFixtureSetUp() {
		await base.TestFixtureSetUp();
		_node = new MiniNode<TLogFormat, TStreamId>(PathName);
		await _node.Start();
	}

	[OneTimeTearDown]
	public override async Task TestFixtureTearDown() {
		await _node.Shutdown();
		await base.TestFixtureTearDown();
	}

	virtual protected IEventStoreConnection BuildConnection(MiniNode<TLogFormat, TStreamId> node) {
		return TestConnection.Create(node.TcpEndPoint);
	}

	[Test]
	[Category("Network")]
	public async Task throw_if_count_le_zero() {
		const string stream = "read_event_stream_forward_should_throw_if_count_le_zero";
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();
			await AssertEx.ThrowsAsync<ArgumentOutOfRangeException>(() =>
				store.ReadStreamEventsForwardAsync(stream, 0, 0, resolveLinkTos: false));
		}
	}

	[Test]
	[Category("Network")]
	public async Task throw_if_start_lt_zero() {
		const string stream = "read_event_stream_forward_should_throw_if_start_lt_zero";
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();
			await AssertEx.ThrowsAsync<ArgumentOutOfRangeException>(() =>
				store.ReadStreamEventsForwardAsync(stream, -1, 1, resolveLinkTos: false));
		}
	}

	[Test]
	[Category("Network")]
	public async Task notify_using_status_code_if_stream_not_found() {
		const string stream = "read_event_stream_forward_should_notify_using_status_code_if_stream_not_found";
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();
			var read = await store.ReadStreamEventsForwardAsync(stream, 0, 1, resolveLinkTos: false);

			Assert.That(read.Status, Is.EqualTo(SliceReadStatus.StreamNotFound));
		}
	}

	[Test]
	[Category("Network")]
	public async Task notify_using_status_code_if_stream_was_deleted() {
		const string stream = "read_event_stream_forward_should_notify_using_status_code_if_stream_was_deleted";
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();
			var delete = await store.DeleteStreamAsync(stream, ExpectedVersion.NoStream, hardDelete: true);

			var read = await store.ReadStreamEventsForwardAsync(stream, 0, 1, resolveLinkTos: false);

			Assert.That(read.Status, Is.EqualTo(SliceReadStatus.StreamDeleted));
		}
	}

	[Test]
	[Category("Network")]
	public async Task return_no_events_when_called_on_empty_stream() {
		const string stream = "read_event_stream_forward_should_return_single_event_when_called_on_empty_stream";
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();

			var read = await store.ReadStreamEventsForwardAsync(stream, 0, 1, resolveLinkTos: false);

			Assert.That(read.Events.Length, Is.EqualTo(0));
		}
	}

	[Test]
	[Category("Network")]
	public async Task return_empty_slice_when_called_on_non_existing_range() {
		const string stream =
			"read_event_stream_forward_should_return_empty_slice_when_called_on_non_existing_range";
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();

			var write10 = await store.AppendToStreamAsync(stream,
				ExpectedVersion.NoStream,
				Enumerable.Range(0, 10).Select(x =>
					TestEvent.NewTestEvent((x + 1).ToString(CultureInfo.InvariantCulture))));

			var read = await store.ReadStreamEventsForwardAsync(stream, 11, 5, resolveLinkTos: false);

			Assert.That(read.Events.Length, Is.EqualTo(0));
		}
	}

	[Test]
	[Category("Network")]
	public async Task return_partial_slice_if_not_enough_events_in_stream() {
		const string stream = "read_event_stream_forward_should_return_partial_slice_if_no_enough_events_in_stream";
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();

			var write10 = await store.AppendToStreamAsync(stream,
				ExpectedVersion.NoStream,
				Enumerable.Range(0, 10).Select(x =>
					TestEvent.NewTestEvent((x + 1).ToString(CultureInfo.InvariantCulture))));

			var read = await store.ReadStreamEventsForwardAsync(stream, 9, 5, resolveLinkTos: false);

			Assert.That(read.Events.Length, Is.EqualTo(1));
		}
	}

	[Test]
	[Category("Network")]
	public async Task throw_when_got_int_max_value_as_maxcount() {
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();

			await AssertEx.ThrowsAsync<ArgumentException>(() =>
				store.ReadStreamEventsForwardAsync("foo", StreamPosition.Start, int.MaxValue,
					resolveLinkTos: false));
		}
	}

	[Test]
	[Category("Network")]
	public async Task return_events_in_same_order_as_written() {
		const string stream = "read_event_stream_forward_should_return_events_in_same_order_as_written";
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();

			var testEvents = Enumerable.Range(0, 10).Select(x => TestEvent.NewTestEvent(x.ToString())).ToArray();
			var write10 = await store.AppendToStreamAsync(stream, ExpectedVersion.NoStream, testEvents);

			var read = await store.ReadStreamEventsForwardAsync(stream, StreamPosition.Start, testEvents.Length,
				resolveLinkTos: false);

			Assert.That(EventDataComparer.Equal(testEvents, read.Events.Select(x => x.Event).ToArray()));
		}
	}

	[Test]
	[Category("Network")]
	public async Task be_able_to_read_single_event_from_arbitrary_position() {
		const string stream = "read_event_stream_forward_should_be_able_to_read_from_arbitrary_position";
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();

			var testEvents = Enumerable.Range(0, 10).Select(x => TestEvent.NewTestEvent(x.ToString())).ToArray();
			var write10 = await store.AppendToStreamAsync(stream, ExpectedVersion.NoStream, testEvents);

			var read = await store.ReadStreamEventsForwardAsync(stream, 5, 1, resolveLinkTos: false);

			Assert.That(EventDataComparer.Equal(testEvents[5], read.Events.Single().Event));
		}
	}

	[Test]
	[Category("Network")]
	public async Task be_able_to_read_slice_from_arbitrary_position() {
		const string stream = "read_event_stream_forward_should_be_able_to_read_slice_from_arbitrary_position";
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();

			var testEvents = Enumerable.Range(0, 10).Select(x => TestEvent.NewTestEvent(x.ToString())).ToArray();
			var write10 = await store.AppendToStreamAsync(stream, ExpectedVersion.NoStream, testEvents);

			var read = await store.ReadStreamEventsForwardAsync(stream, 5, 2, resolveLinkTos: false);

			Assert.That(EventDataComparer.Equal(testEvents.Skip(5).Take(2).ToArray(),
				read.Events.Select(x => x.Event).ToArray()));
		}
	}
}
