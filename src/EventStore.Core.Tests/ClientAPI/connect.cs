// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Net;
using System.Threading.Tasks;
using EventStore.ClientAPI;
using EventStore.ClientAPI.Internal;
using EventStore.Core.Tests.ClientAPI.Helpers;
using EventStore.Core.Tests.Helpers;
using NUnit.Framework;

namespace EventStore.Core.Tests.ClientAPI;

[Category("ClientAPI"), Category("LongRunning")]
[TestFixture(typeof(LogFormat.V2), typeof(string), TcpType.Normal)]
[TestFixture(typeof(LogFormat.V3), typeof(uint), TcpType.Normal)]
[TestFixture(typeof(LogFormat.V2), typeof(string), TcpType.Ssl)]
[TestFixture(typeof(LogFormat.V3), typeof(uint), TcpType.Ssl)]
public class Connect<TLogFormat, TStreamId> : SpecificationWithDirectoryPerTestFixture {
	private readonly TcpType _tcpType;

	public Connect(TcpType tcpType) {
		_tcpType = tcpType;
	}

	//TODO GFY THESE NEED TO BE LOOKED AT IN LINUX
	[Test, Category("Network")]
	public async Task should_not_throw_exception_when_server_is_down() {
		var ip = IPAddress.Loopback;
		int port = PortsHelper.GetAvailablePort(ip);
		using var connection = TestConnection.Create(new IPEndPoint(ip, port), _tcpType);
		await connection.ConnectAsync();
	}

	//TODO GFY THESE NEED TO BE LOOKED AT IN LINUX
	[Test, Category("Network")]
	public async Task should_throw_exception_when_trying_to_reopen_closed_connection() {
		ClientApiLoggerBridge.Default.Info("Starting '{0}' test...",
			"should_throw_exception_when_trying_to_reopen_closed_connection");

		var closed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var settings = ConnectionSettings.Create()
			.EnableVerboseLogging()
			.UseCustomLogger(ClientApiLoggerBridge.Default)
			.LimitReconnectionsTo(0)
			.WithConnectionTimeoutOf(TimeSpan.FromSeconds(10))
			.SetReconnectionDelayTo(TimeSpan.FromMilliseconds(0))
			.FailOnNoServerResponse();
		if (_tcpType == TcpType.Ssl) {
			settings.DisableServerCertificateValidation();
		} else {
			settings.DisableTls();
		}

		var ip = IPAddress.Loopback;
		int port = PortsHelper.GetAvailablePort(ip);
		using var connection = EventStoreConnection.Create(settings, new IPEndPoint(ip, port).ToESTcpUri());
		connection.Closed += (s, e) => closed.TrySetResult(true);

		await connection.ConnectAsync();

		await closed.Task.WithTimeout(
			TimeSpan.FromSeconds(120)); // TCP connection timeout might be even 60 seconds

		await AssertEx.ThrowsAsync<ObjectDisposedException>(() => connection.ConnectAsync().WithTimeout());
	}

	//TODO GFY THIS TEST TIMES OUT IN LINUX.
	[Test, Category("Network")]
	public async Task should_close_connection_after_configured_amount_of_failed_reconnections() {
		var closed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var settings =
			ConnectionSettings.Create()
				.EnableVerboseLogging()
				.UseCustomLogger(ClientApiLoggerBridge.Default)
				.LimitReconnectionsTo(1)
				.WithConnectionTimeoutOf(TimeSpan.FromSeconds(10))
				.SetReconnectionDelayTo(TimeSpan.FromMilliseconds(0))
				.FailOnNoServerResponse();
		if (_tcpType == TcpType.Ssl) {
			settings.DisableServerCertificateValidation();
		} else {
			settings.DisableTls();
		}

		var ip = IPAddress.Loopback;
		int port = PortsHelper.GetAvailablePort(ip);
		using var connection = EventStoreConnection.Create(settings, new IPEndPoint(ip, port).ToESTcpUri());
		connection.Closed += (s, e) => closed.TrySetResult(true);
		connection.Connected += (s, e) =>
			Console.WriteLine("EventStoreConnection '{0}': connected to [{1}]...",
				e.Connection.ConnectionName, e.RemoteEndPoint);
		connection.Reconnecting += (s, e) =>
			Console.WriteLine("EventStoreConnection '{0}': reconnecting...", e.Connection.ConnectionName);
		connection.Disconnected += (s, e) =>
			Console.WriteLine("EventStoreConnection '{0}': disconnected from [{1}]...",
				e.Connection.ConnectionName, e.RemoteEndPoint);
		connection.ErrorOccurred += (s, e) => Console.WriteLine("EventStoreConnection '{0}': error = {1}",
			e.Connection.ConnectionName, e.Exception);

		await connection.ConnectAsync();

		await closed.Task.WithTimeout(
			TimeSpan.FromSeconds(120)); // TCP connection timeout might be even 60 seconds

		await AssertEx.ThrowsAsync<ObjectDisposedException>(() => connection
			.AppendToStreamAsync("stream", ExpectedVersion.NoStream, TestEvent.NewTestEvent())
			.WithTimeout());
	}
}

[TestFixture, Category("ClientAPI"), Category("LongRunning")]
public class not_connected_tests {
	private readonly TcpType _tcpType = TcpType.Ssl;

	[Test]
	public async Task should_timeout_connection_after_configured_amount_time_on_conenct() {
		var closed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var settings =
			ConnectionSettings.Create()
				.EnableVerboseLogging()
				.UseCustomLogger(ClientApiLoggerBridge.Default)
				.LimitReconnectionsTo(0)
				.SetReconnectionDelayTo(TimeSpan.FromMilliseconds(0))
				.FailOnNoServerResponse()
				.WithConnectionTimeoutOf(TimeSpan.FromMilliseconds(1000));

		if (_tcpType == TcpType.Ssl) {
			settings.DisableServerCertificateValidation();
		} else {
			settings.DisableTls();
		}

		var ip = new IPAddress(new byte[]
			{8, 8, 8, 8}); //NOTE: This relies on Google DNS server being configured to swallow nonsense traffic
		const int port = 4567;
		using (var connection = EventStoreConnection.Create(settings, new IPEndPoint(ip, port).ToESTcpUri())) {
			connection.Closed += (s, e) => closed.TrySetResult(true);
			connection.Connected += (s, e) => Console.WriteLine("EventStoreConnection '{0}': connected to [{1}]...",
				e.Connection.ConnectionName, e.RemoteEndPoint);
			connection.Reconnecting += (s, e) =>
				Console.WriteLine("EventStoreConnection '{0}': reconnecting...", e.Connection.ConnectionName);
			connection.Disconnected += (s, e) =>
				Console.WriteLine("EventStoreConnection '{0}': disconnected from [{1}]...",
					e.Connection.ConnectionName, e.RemoteEndPoint);
			connection.ErrorOccurred += (s, e) => Console.WriteLine("EventStoreConnection '{0}': error = {1}",
				e.Connection.ConnectionName, e.Exception);
			await connection.ConnectAsync();

			await closed.Task.WithTimeout(TimeSpan.FromSeconds(15));
		}
	}
}
