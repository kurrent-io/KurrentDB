// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading.Tasks;
using EventStore.ClientAPI;
using EventStore.ClientAPI.Exceptions;
using KurrentDB.Core.Tests.ClientAPI.Helpers;
using KurrentDB.Core.Tests.Helpers;
using NUnit.Framework;

namespace KurrentDB.Core.Tests.ClientAPI;

[Category("ClientAPI"), Category("LongRunning")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint), Ignore = "Explicit transactions are not supported yet by Log V3")]
public class when_committing_empty_transaction<TLogFormat, TStreamId> : SpecificationWithDirectory {
	private MiniNode<TLogFormat, TStreamId> _node;
	private IEventStoreConnection _connection;
	private EventData _firstEvent;

	[SetUp]
	public override async Task SetUp() {
		await base.SetUp();
		_node = new MiniNode<TLogFormat, TStreamId>(PathName);
		await _node.Start();

		_firstEvent = TestEvent.NewTestEvent();

		_connection = BuildConnection(_node);
		await _connection.ConnectAsync();

		Assert.AreEqual(2, (await _connection.AppendToStreamAsync("test-stream",
			ExpectedVersion.NoStream,
			_firstEvent,
			TestEvent.NewTestEvent(),
			TestEvent.NewTestEvent())).NextExpectedVersion);

		using (var transaction = await _connection.StartTransactionAsync("test-stream", 2)) {
			Assert.AreEqual(2, (await transaction.CommitAsync()).NextExpectedVersion);
		}
	}

	[TearDown]
	public override async Task TearDown() {
		_connection.Close();
		await _node.Shutdown();
		await base.TearDown();
	}

	protected virtual IEventStoreConnection BuildConnection(MiniNode<TLogFormat, TStreamId> node) {
		return TestConnection.Create(node.TcpEndPoint);
	}

	[Test]
	public async Task following_append_with_correct_expected_version_are_committed_correctly() {
		Assert.AreEqual(4,
			(await _connection.AppendToStreamAsync("test-stream", 2, TestEvent.NewTestEvent(),
				TestEvent.NewTestEvent())
			).NextExpectedVersion);

		var res = await _connection.ReadStreamEventsForwardAsync("test-stream", 0, 100, false);
		Assert.AreEqual(SliceReadStatus.Success, res.Status);
		Assert.AreEqual(5, res.Events.Length);
		for (int i = 0; i < 5; ++i) {
			Assert.AreEqual(i, res.Events[i].OriginalEventNumber);
		}
	}

	[Test]
	public async Task following_append_with_expected_version_any_are_committed_correctly() {
		Assert.AreEqual(4,
			(await _connection.AppendToStreamAsync("test-stream", ExpectedVersion.Any, TestEvent.NewTestEvent(),
				TestEvent.NewTestEvent())).NextExpectedVersion);

		var res = await _connection.ReadStreamEventsForwardAsync("test-stream", 0, 100, false);
		Assert.AreEqual(SliceReadStatus.Success, res.Status);
		Assert.AreEqual(5, res.Events.Length);
		for (int i = 0; i < 5; ++i) {
			Assert.AreEqual(i, res.Events[i].OriginalEventNumber);
		}
	}

	[Test]
	public async Task committing_first_event_with_expected_version_no_stream_is_idempotent() {
		Assert.AreEqual(0,
			(await _connection.AppendToStreamAsync("test-stream", ExpectedVersion.NoStream, _firstEvent))
			.NextExpectedVersion);

		var res = await _connection.ReadStreamEventsForwardAsync("test-stream", 0, 100, false);
		Assert.AreEqual(SliceReadStatus.Success, res.Status);
		Assert.AreEqual(3, res.Events.Length);
		for (int i = 0; i < 3; ++i) {
			Assert.AreEqual(i, res.Events[i].OriginalEventNumber);
		}
	}

	[Test]
	public async Task trying_to_append_new_events_with_expected_version_no_stream_fails() {
		await AssertEx.ThrowsAsync<WrongExpectedVersionException>(() =>
			_connection.AppendToStreamAsync("test-stream", ExpectedVersion.NoStream, TestEvent.NewTestEvent()));
	}
}
