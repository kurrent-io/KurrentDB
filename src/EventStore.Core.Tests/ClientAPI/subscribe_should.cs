// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading;
using System.Threading.Tasks;
using EventStore.ClientAPI;
using EventStore.Core.Tests.ClientAPI.Helpers;
using EventStore.Core.Tests.Helpers;
using NUnit.Framework;

namespace EventStore.Core.Tests.ClientAPI;

[Category("ClientAPI"), Category("LongRunning")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class subscribe_should<TLogFormat, TStreamId> : SpecificationWithDirectoryPerTestFixture {
	private const int Timeout = 10000;

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

	[Test, Category("LongRunning")]
	public async Task be_able_to_subscribe_to_non_existing_stream_and_then_catch_new_event() {
		const string stream =
			"subscribe_should_be_able_to_subscribe_to_non_existing_stream_and_then_catch_created_event";
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();
			var appeared = new CountdownEvent(1);
			var dropped = new CountdownEvent(1);

			using (await store.SubscribeToStreamAsync(stream, false, (s, x) => {
				appeared.Signal();
				return;
			}, (s, r, e) => dropped.Signal())) {
				await store.AppendToStreamAsync(stream, ExpectedVersion.NoStream, TestEvent.NewTestEvent());
				Assert.IsTrue(appeared.Wait(Timeout), "Appeared countdown event timed out.");
			}
		}
	}

	[Test, Category("LongRunning")]
	public async Task allow_multiple_subscriptions_to_same_stream() {
		const string stream = "subscribe_should_allow_multiple_subscriptions_to_same_stream";
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();
			var appeared = new CountdownEvent(2);
			var dropped = new CountdownEvent(2);

			using (await store.SubscribeToStreamAsync(stream, false, (s, x) => {
				appeared.Signal();
				return;
			}, (s, r, e) => dropped.Signal()))
			using (await store.SubscribeToStreamAsync(stream, false, (s, x) => {
				appeared.Signal();
				return;
			}, (s, r, e) => dropped.Signal())) {
				await store.AppendToStreamAsync(stream, ExpectedVersion.NoStream, TestEvent.NewTestEvent());
				Assert.IsTrue(appeared.Wait(Timeout), "Appeared countdown event timed out.");
			}
		}
	}

	[Test, Category("LongRunning")]
	public async Task call_dropped_callback_after_unsubscribe_method_call() {
		const string stream = "subscribe_should_call_dropped_callback_after_unsubscribe_method_call";
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();

			var dropped = new CountdownEvent(1);
			using (var subscription = await store.SubscribeToStreamAsync(stream, false, (s, x) => Task.CompletedTask,
				(s, r, e) => dropped.Signal())) {
				subscription.Unsubscribe();
			}

			Assert.IsTrue(dropped.Wait(Timeout), "Dropped countdown event timed out.");
		}
	}

	[Test, Category("LongRunning")]
	public async Task catch_deleted_events_as_well() {
		const string stream = "subscribe_should_catch_created_and_deleted_events_as_well";
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();

			var appeared = new CountdownEvent(1);
			var dropped = new CountdownEvent(1);
			using (await store.SubscribeToStreamAsync(stream, false, (s, x) => {
				appeared.Signal();
				return;
			},
				(s, r, e) => dropped.Signal())) {
				await store.DeleteStreamAsync(stream, ExpectedVersion.NoStream, hardDelete: true);
				Assert.IsTrue(appeared.Wait(Timeout), "Appeared countdown event timed out.");
			}
		}
	}
}
