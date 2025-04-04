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
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class appending_to_implicitly_created_stream<TLogFormat, TStreamId> : SpecificationWithDirectoryPerTestFixture {
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


	protected virtual IEventStoreConnection BuildConnection(MiniNode<TLogFormat, TStreamId> node) {
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
			"appending_to_implicitly_created_stream_sequence_0em1_1e0_2e1_3e2_4e3_5e4_0em1_idempotent";
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();

			var events = Enumerable.Range(0, 6).Select(x => TestEvent.NewTestEvent(Guid.NewGuid())).ToArray();
			var writer = new StreamWriter(store, stream, -1);

			var tail = await writer.Append(events);

			await tail.Then(events[0], -1);

			var total = await EventsStream.Count(store, stream);
			Assert.That(total, Is.EqualTo(events.Length));
		}
	}

	[Test]
	[Category("Network")]
	public async Task sequence_0em1_1e0_2e1_3e2_4e3_4e4_0any_idempotent() {
		const string stream =
			"appending_to_implicitly_created_stream_sequence_0em1_1e0_2e1_3e2_4e3_4e4_0any_idempotent";
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();

			var events = Enumerable.Range(0, 6).Select(x => TestEvent.NewTestEvent(Guid.NewGuid())).ToArray();
			var writer = new StreamWriter(store, stream, -1);

			var tail = await writer.Append(events);
			await tail.Then(events.First(), ExpectedVersion.Any);

			var total = await EventsStream.Count(store, stream);
			Assert.That(total, Is.EqualTo(events.Length));
		}
	}

	[Test]
	[Category("Network")]
	public async Task sequence_0em1_1e0_2e1_3e2_4e3_5e4_0e5_non_idempotent() {
		const string stream =
			"appending_to_implicitly_created_stream_sequence_0em1_1e0_2e1_3e2_4e3_5e4_0e5_non_idempotent";
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();

			var events = Enumerable.Range(0, 6).Select(x => TestEvent.NewTestEvent(Guid.NewGuid())).ToArray();
			var writer = new StreamWriter(store, stream, -1);

			var tail = await writer.Append(events);
			await tail.Then(events.First(), 5);

			var total = await EventsStream.Count(store, stream);
			Assert.That(total, Is.EqualTo(events.Length + 1));
		}
	}

	[Test]
	[Category("Network")]
	public async Task sequence_0em1_1e0_2e1_3e2_4e3_5e4_0e6_wev() {
		const string stream = "appending_to_implicitly_created_stream_sequence_0em1_1e0_2e1_3e2_4e3_5e4_0e6_wev";
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();

			var events = Enumerable.Range(0, 6).Select(x => TestEvent.NewTestEvent(Guid.NewGuid())).ToArray();
			var writer = new StreamWriter(store, stream, -1);

			var first6 = await writer.Append(events);
			await AssertEx.ThrowsAsync<WrongExpectedVersionException>(
				() => first6.Then(events.First(), 6));
		}
	}

	[Test]
	[Category("Network")]
	public async Task sequence_0em1_1e0_2e1_3e2_4e3_5e4_0e4_wev() {
		const string stream = "appending_to_implicitly_created_stream_sequence_0em1_1e0_2e1_3e2_4e3_5e4_0e4_wev";
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();

			var events = Enumerable.Range(0, 6).Select(x => TestEvent.NewTestEvent(Guid.NewGuid())).ToArray();
			var writer = new StreamWriter(store, stream, -1);

			var first6 = await writer.Append(events);
			await AssertEx.ThrowsAsync<WrongExpectedVersionException>(
				() => first6.Then(events.First(), 4));
		}
	}

	[Test]
	[Category("Network")]
	public async Task sequence_0em1_0e0_non_idempotent() {
		const string stream = "appending_to_implicitly_created_stream_sequence_0em1_0e0_non_idempotent";
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();

			var events = Enumerable.Range(0, 1).Select(x => TestEvent.NewTestEvent(Guid.NewGuid())).ToArray();
			var writer = new StreamWriter(store, stream, -1);

			var tail = await writer.Append(events);
			await tail.Then(events.First(), 0);

			var total = await EventsStream.Count(store, stream);
			Assert.That(total, Is.EqualTo(events.Length + 1));
		}
	}

	[Test]
	[Category("Network")]
	public async Task sequence_0em1_0any_idempotent() {
		const string stream = "appending_to_implicitly_created_stream_sequence_0em1_0any_idempotent";
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();

			var events = Enumerable.Range(0, 1).Select(x => TestEvent.NewTestEvent(Guid.NewGuid())).ToArray();
			var writer = new StreamWriter(store, stream, -1);

			var tail = await writer.Append(events);

			await tail.Then(events.First(), ExpectedVersion.Any);

			var total = await EventsStream.Count(store, stream);
			Assert.That(total, Is.EqualTo(events.Length));
		}
	}

	[Test]
	[Category("Network")]
	public async Task sequence_0em1_0em1_idempotent() {
		const string stream = "appending_to_implicitly_created_stream_sequence_0em1_0em1_idempotent";
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();

			var events = Enumerable.Range(0, 1).Select(x => TestEvent.NewTestEvent(Guid.NewGuid())).ToArray();
			var writer = new StreamWriter(store, stream, -1);

			var tail = await writer.Append(events);
			await tail.Then(events.First(), -1);

			var total = await EventsStream.Count(store, stream);
			Assert.That(total, Is.EqualTo(events.Length));
		}
	}

	[Test]
	[Category("Network")]
	public async Task sequence_0em1_1e0_2e1_1any_1any_idempotent() {
		const string stream = "appending_to_implicitly_created_stream_sequence_0em1_1e0_2e1_1any_1any_idempotent";
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();

			var events = Enumerable.Range(0, 3).Select(x => TestEvent.NewTestEvent(Guid.NewGuid())).ToArray();
			var writer = new StreamWriter(store, stream, -1);


			var tailWriter = await writer.Append(events);
			tailWriter = await tailWriter.Then(events[1], ExpectedVersion.Any);
			await tailWriter.Then(events[1], ExpectedVersion.Any);

			var total = await EventsStream.Count(store, stream);
			Assert.That(total, Is.EqualTo(events.Length));
		}
	}

	[Test]
	[Category("Network")]
	public async Task sequence_S_0em1_1em1_E_S_0em1_E_idempotent() {
		const string stream = "appending_to_implicitly_created_stream_sequence_S_0em1_1em1_E_S_0em1_E_idempotent";
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();

			var events = Enumerable.Range(0, 2).Select(x => TestEvent.NewTestEvent(Guid.NewGuid())).ToArray();
			await store.AppendToStreamAsync(stream, -1, events);

			await store.AppendToStreamAsync(stream, -1, events.First());

			var total = await EventsStream.Count(store, stream);
			Assert.That(total, Is.EqualTo(events.Length));
		}
	}

	[Test]
	[Category("Network")]
	public async Task sequence_S_0em1_1em1_E_S_0any_E_idempotent() {
		const string stream = "appending_to_implicitly_created_stream_sequence_S_0em1_1em1_E_S_0any_E_idempotent";
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();

			var events = Enumerable.Range(0, 2).Select(x => TestEvent.NewTestEvent(Guid.NewGuid())).ToArray();
			await store.AppendToStreamAsync(stream, -1, events);

			await store.AppendToStreamAsync(stream, ExpectedVersion.Any, new[] { events.First() });

			var total = await EventsStream.Count(store, stream);
			Assert.That(total, Is.EqualTo(events.Length));
		}
	}

	[Test]
	[Category("Network")]
	public async Task sequence_S_0em1_1em1_E_S_1e0_E_idempotent() {
		const string stream = "appending_to_implicitly_created_stream_sequence_S_0em1_1em1_E_S_1e0_E_idempotent";
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();

			var events = Enumerable.Range(0, 2).Select(x => TestEvent.NewTestEvent(Guid.NewGuid())).ToArray();
			await store.AppendToStreamAsync(stream, -1, events);

			await store.AppendToStreamAsync(stream, 0, events[1]);

			var total = await EventsStream.Count(store, stream);
			Assert.That(total, Is.EqualTo(events.Length));
		}
	}

	[Test]
	[Category("Network")]
	public async Task sequence_S_0em1_1em1_E_S_1any_E_idempotent() {
		const string stream = "appending_to_implicitly_created_stream_sequence_S_0em1_1em1_E_S_1any_E_idempotent";
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();

			var events = Enumerable.Range(0, 2).Select(x => TestEvent.NewTestEvent(Guid.NewGuid())).ToArray();
			await store.AppendToStreamAsync(stream, -1, events);

			await store.AppendToStreamAsync(stream, ExpectedVersion.Any, events[1]);

			var total = await EventsStream.Count(store, stream);
			Assert.That(total, Is.EqualTo(events.Length));
		}
	}

	[Test]
	[Category("Network")]
	public async Task sequence_S_0em1_1em1_E_S_0em1_1em1_2em1_E_idempotancy_fail() {
		const string stream =
			"appending_to_implicitly_created_stream_sequence_S_0em1_1em1_E_S_0em1_1em1_2em1_E_idempotancy_fail";
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();

			var events = Enumerable.Range(0, 2).Select(x => TestEvent.NewTestEvent(Guid.NewGuid())).ToArray();
			await store.AppendToStreamAsync(stream, -1, events);

			await AssertEx.ThrowsAsync<WrongExpectedVersionException>(
				() => store.AppendToStreamAsync(stream, -1,
					events.Concat(new[] { TestEvent.NewTestEvent(Guid.NewGuid()) })));
		}
	}
}
