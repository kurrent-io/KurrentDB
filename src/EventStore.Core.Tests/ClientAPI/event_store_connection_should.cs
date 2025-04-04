// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading.Tasks;
using EventStore.ClientAPI;
using EventStore.Core.Tests.ClientAPI.Helpers;
using EventStore.Core.Tests.Helpers;
using NUnit.Framework;

namespace EventStore.Core.Tests.ClientAPI;

[Category("ClientAPI"), Category("LongRunning")]
[TestFixture(typeof(LogFormat.V2), typeof(string), TcpType.Normal)]
[TestFixture(typeof(LogFormat.V3), typeof(uint), TcpType.Normal)]
[TestFixture(typeof(LogFormat.V2), typeof(string), TcpType.Ssl)]
[TestFixture(typeof(LogFormat.V3), typeof(uint), TcpType.Ssl)]
public class event_store_connection_should<TLogFormat, TStreamId> : SpecificationWithDirectoryPerTestFixture {
	private readonly TcpType _tcpType;
	private MiniNode<TLogFormat, TStreamId> _node;

	public event_store_connection_should(TcpType tcpType) {
		_tcpType = tcpType;
	}

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

	[Test]
	[Category("Network")]
	public void not_throw_on_close_if_connect_was_not_called() {
		var connection = TestConnection.To(_node, _tcpType);
		Assert.DoesNotThrow(connection.Close);
	}

	[Test]
	[Category("Network")]
	public async Task not_throw_on_close_if_called_multiple_times() {
		var connection = TestConnection.To(_node, _tcpType);
		await connection.ConnectAsync();
		connection.Close();
		Assert.DoesNotThrow(connection.Close);
	}

	/*
	//TODO WEIRD TEST GFY
			[Test]
			[Category("Network")]
			public void throw_on_connect_called_more_than_once()
			{
				var connection = TestConnection.To(_node, _tcpType);
				Assert.DoesNotThrow(() => connection.ConnectAsync().Wait());

				await AssertEx.ThrowsAsync<>(() => connection.ConnectAsync().Wait(),
							Throws.Exception.InstanceOf<AggregateException>().With.InnerException.InstanceOf<InvalidOperationException>());
			}

			[Test]
			[Category("Network")]
			public void throw_on_connect_called_after_close()
			{
				var connection = TestConnection.To(_node, _tcpType);
				connection.ConnectAsync().Wait();
				connection.Close();

				await AssertEx.ThrowsAsync<>(() => connection.ConnectAsync().Wait(),
							Throws.Exception.InstanceOf<AggregateException>().With.InnerException.InstanceOf<InvalidOperationException>());
			}
	*/
	[Test]
	[Category("Network")]
	public async Task throw_invalid_operation_on_every_api_call_if_connect_was_not_called() {
		var connection = TestConnection.To(_node, _tcpType);

		const string s = "stream";
		var events = new[] { TestEvent.NewTestEvent() };

		await AssertEx.ThrowsAsync<InvalidOperationException>(() => connection.DeleteStreamAsync(s, 0));

		await AssertEx.ThrowsAsync<InvalidOperationException>(() => connection.AppendToStreamAsync(s, 0, events));

		await AssertEx.ThrowsAsync<InvalidOperationException>(
			() => connection.ReadStreamEventsForwardAsync(s, 0, 1, resolveLinkTos: false));

		await AssertEx.ThrowsAsync<InvalidOperationException>(
			() => connection.ReadStreamEventsBackwardAsync(s, 0, 1, resolveLinkTos: false));

		await AssertEx.ThrowsAsync<InvalidOperationException>(
			() => connection.ReadAllEventsForwardAsync(Position.Start, 1, false));

		await AssertEx.ThrowsAsync<InvalidOperationException>(() =>
			connection.ReadAllEventsBackwardAsync(Position.End, 1, false));

		await AssertEx.ThrowsAsync<InvalidOperationException>(() => connection.StartTransactionAsync(s, 0));

		await AssertEx.ThrowsAsync<InvalidOperationException>(
			() => connection.SubscribeToStreamAsync(s, false, (_, __) => Task.CompletedTask, (_, __, ___) => { }));

		await AssertEx.ThrowsAsync<InvalidOperationException>(
			() => connection.SubscribeToAllAsync(false, (_, __) => Task.CompletedTask, (_, __, ___) => { }));
	}
}
