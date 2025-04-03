// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using System.Threading.Tasks;
using EventStore.ClientAPI;
using EventStore.ClientAPI.Exceptions;
using EventStore.Core.Tests.ClientAPI.Helpers;
using EventStore.Core.Tests.Helpers;
using NUnit.Framework;

namespace EventStore.Core.Tests.ClientAPI;

[Category("ClientAPI"), Category("LongRunning")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint), Ignore = "Explicit transactions are not supported yet by Log V3")]
public class appending_to_implicitly_created_stream_using_transaction<TLogFormat, TStreamId> : SpecificationWithDirectoryPerTestFixture {
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

	/*
	 * sequence - events written so stream
	 * 0em1 - event number 0 written with exp version -1 (minus 1)
	 * 1any - event number 1 written with exp version any
	 * S_0em1_1em1_E - START bucket, two events in bucket, END bucket
	*/

	[Test]
	[Category("Network")]
	public async Task sequence_0em1_1e0_2e1_3e2_4e3_5e4_0em1_idempotent() {
		const string stream =
			"appending_to_implicitly_created_stream_using_transaction_sequence_0em1_1e0_2e1_3e2_4e3_5e4_0em1_idempotent";
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();

			var events = Enumerable.Range(0, 6).Select(x => TestEvent.NewTestEvent(Guid.NewGuid())).ToArray();
			var writer = new TransactionalWriter(store, stream);

			Assert.AreEqual(5, (await (await (await writer.StartTransaction(-1)).Write(events)).Commit()).NextExpectedVersion);
			Assert.AreEqual(0, (await (await (await writer.StartTransaction(-1)).Write(events.First())).Commit()).NextExpectedVersion);

			var total = await EventsStream.Count(store, stream);
			Assert.That(total, Is.EqualTo(events.Length));
		}
	}

	[Test]
	[Category("Network")]
	public async Task sequence_0em1_1e0_2e1_3e2_4e3_5e4_0any_idempotent() {
		const string stream =
			"appending_to_implicitly_created_stream_using_transaction_sequence_0em1_1e0_2e1_3e2_4e3_5e4_0any_idempotent";
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();

			var events = Enumerable.Range(0, 6).Select(x => TestEvent.NewTestEvent(Guid.NewGuid())).ToArray();
			var writer = new TransactionalWriter(store, stream);

			Assert.AreEqual(5, (await (await (await writer.StartTransaction(-1)).Write(events)).Commit()).NextExpectedVersion);
			Assert.AreEqual(0, (await (await (await writer.StartTransaction(ExpectedVersion.Any)).Write(events.First())).Commit()).NextExpectedVersion);

			var total = await EventsStream.Count(store, stream);
			Assert.That(total, Is.EqualTo(events.Length));
		}
	}

	[Test]
	[Category("Network")]
	public async Task sequence_0em1_1e0_2e1_3e2_4e3_5e4_0e5_non_idempotent() {
		const string stream =
			"appending_to_implicitly_created_stream_using_transaction_sequence_0em1_1e0_2e1_3e2_4e3_5e4_0e5_non_idempotent";
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();

			var events = Enumerable.Range(0, 6).Select(x => TestEvent.NewTestEvent(Guid.NewGuid())).ToArray();
			var writer = new TransactionalWriter(store, stream);

			Assert.AreEqual(5, (await (await (await writer.StartTransaction(-1)).Write(events)).Commit()).NextExpectedVersion);
			Assert.AreEqual(6, (await (await (await writer.StartTransaction(5)).Write(events.First())).Commit()).NextExpectedVersion);

			var total = await EventsStream.Count(store, stream);
			Assert.That(total, Is.EqualTo(events.Length + 1));
		}
	}

	[Test]
	[Category("Network")]
	public async Task sequence_0em1_1e0_2e1_3e2_4e3_5e4_0e6_wev() {
		const string stream =
			"appending_to_implicitly_created_stream_using_transaction_sequence_0em1_1e0_2e1_3e2_4e3_5e4_0e6_wev";
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();

			var events = Enumerable.Range(0, 6).Select(x => TestEvent.NewTestEvent(Guid.NewGuid())).ToArray();
			var writer = new TransactionalWriter(store, stream);

			Assert.AreEqual(5, (await (await (await writer.StartTransaction(-1)).Write(events)).Commit()).NextExpectedVersion);
			await AssertEx.ThrowsAsync<WrongExpectedVersionException>(async () =>
				await (await (await writer.StartTransaction(6)).Write(events.First())).Commit());
		}
	}

	[Test]
	[Category("Network")]
	public async Task sequence_0em1_1e0_2e1_3e2_4e3_5e4_0e4_wev() {
		const string stream =
			"appending_to_implicitly_created_stream_using_transaction_sequence_0em1_1e0_2e1_3e2_4e3_5e4_0e4_wev";
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();

			var events = Enumerable.Range(0, 6).Select(x => TestEvent.NewTestEvent(Guid.NewGuid())).ToArray();
			var writer = new TransactionalWriter(store, stream);

			Assert.AreEqual(5, (await (await (await writer.StartTransaction(-1)).Write(events)).Commit()).NextExpectedVersion);
			await AssertEx.ThrowsAsync<WrongExpectedVersionException>(
				async () => await (await (await writer.StartTransaction(4)).Write(events.First())).Commit());
		}
	}

	[Test]
	[Category("Network")]
	public async Task sequence_0em1_0e0_non_idempotent() {
		const string stream =
			"appending_to_implicitly_created_stream_using_transaction_sequence_0em1_0e0_non_idempotent";
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();

			var events = Enumerable.Range(0, 1).Select(x => TestEvent.NewTestEvent(Guid.NewGuid())).ToArray();
			var writer = new TransactionalWriter(store, stream);

			Assert.AreEqual(0, (await (await (await writer.StartTransaction(-1)).Write(events)).Commit()).NextExpectedVersion);
			Assert.AreEqual(1, (await (await (await writer.StartTransaction(0)).Write(events.First())).Commit()).NextExpectedVersion);

			var total = await EventsStream.Count(store, stream);
			Assert.That(total, Is.EqualTo(events.Length + 1));
		}
	}

	[Test]
	[Category("Network")]
	public async Task sequence_0em1_0any_idempotent() {
		const string stream =
			"appending_to_implicitly_created_stream_using_transaction_sequence_0em1_0any_idempotent";
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();

			var events = Enumerable.Range(0, 1).Select(x => TestEvent.NewTestEvent(Guid.NewGuid())).ToArray();
			var writer = new TransactionalWriter(store, stream);

			Assert.AreEqual(0,
				(await (await (await writer.StartTransaction(-1)).Write(events)).Commit()).NextExpectedVersion);
			Assert.AreEqual(0,
				(await (await (await writer.StartTransaction(ExpectedVersion.Any)).Write(events.First())).Commit())
				.NextExpectedVersion);

			var total = await EventsStream.Count(store, stream);
			Assert.That(total, Is.EqualTo(events.Length));
		}
	}

	[Test]
	[Category("Network")]
	public async Task sequence_0em1_0em1_idempotent() {
		const string stream =
			"appending_to_implicitly_created_stream_using_transaction_sequence_0em1_0em1_idempotent";
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();

			var events = Enumerable.Range(0, 1).Select(x => TestEvent.NewTestEvent(Guid.NewGuid())).ToArray();
			var writer = new TransactionalWriter(store, stream);

			Assert.AreEqual(0, (await (await (await writer.StartTransaction(-1)).Write(events)).Commit()).NextExpectedVersion);
			Assert.AreEqual(0, (await (await (await writer.StartTransaction(-1)).Write(events.First())).Commit()).NextExpectedVersion);

			var total = await EventsStream.Count(store, stream);
			Assert.That(total, Is.EqualTo(events.Length));
		}
	}

	[Test]
	[Category("Network")]
	public async Task sequence_0em1_1e0_2e1_1any_1any_idempotent() {
		const string stream =
			"appending_to_implicitly_created_stream_using_transaction_sequence_0em1_1e0_2e1_1any_1any_idempotent";
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();

			var events = Enumerable.Range(0, 3).Select(x => TestEvent.NewTestEvent(Guid.NewGuid())).ToArray();
			var writer = new TransactionalWriter(store, stream);

			Assert.AreEqual(2, (await (await (await writer.StartTransaction(-1)).Write(events)).Commit()).NextExpectedVersion);
			Assert.AreEqual(1, (await (await (await (await writer.StartTransaction(ExpectedVersion.Any)).Write(events[1])).Write(events[1])).Commit()).NextExpectedVersion);

			var total = await EventsStream.Count(store, stream);
			Assert.That(total, Is.EqualTo(events.Length));
		}
	}

	[Test]
	[Category("Network")]
	public async Task sequence_S_0em1_1em1_E_S_0em1_1em1_2em1_E_idempotancy_fail() {
		const string stream =
			"appending_to_implicitly_created_stream_using_transaction_sequence_S_0em1_1em1_E_S_0em1_1em1_2em1_E_idempotancy_fail";
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();

			var events = Enumerable.Range(0, 2).Select(x => TestEvent.NewTestEvent(Guid.NewGuid())).ToArray();
			var writer = new TransactionalWriter(store, stream);

			Assert.AreEqual(1, (await (await (await writer.StartTransaction(-1)).Write(events)).Commit()).NextExpectedVersion);
			await AssertEx.ThrowsAsync<WrongExpectedVersionException>(async () =>
				await (await (await writer.StartTransaction(-1)).Write(events.Concat(new[] { TestEvent.NewTestEvent(Guid.NewGuid()) }).ToArray())).Commit());
		}
	}
}
