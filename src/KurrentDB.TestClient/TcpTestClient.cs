// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Threading;
using EventStore.BufferManagement;
using EventStore.Common.Utils;
using EventStore.Core.Services.Transport.Tcp;
using EventStore.Transport.Tcp;
using EventStore.Transport.Tcp.Formatting;
using EventStore.Transport.Tcp.Framing;
using Connection = EventStore.Transport.Tcp.TcpTypedConnection<byte[]>;
using ILogger = Serilog.ILogger;

namespace KurrentDB.TestClient;

/// <summary>
/// A test client that connects over Raw TCP connections
/// </summary>
public class TcpTestClient {
	private readonly BufferManager _bufferManager =
		new BufferManager(TcpConfiguration.BufferChunksCount, TcpConfiguration.SocketBufferSize);

	private readonly TcpClientConnector _connector = new TcpClientConnector();

	private readonly bool _validateServer;
	private readonly bool _useSsl;
	private readonly ILogger _log;
	private readonly bool _interactiveMode;
	/// <summary>
	/// The TCP EndPoint of the Event Store node
	/// </summary>
	public readonly EndPoint TcpEndpoint;
	/// <summary>
	/// The Client Options used to start KurrentDB.TestClient
	/// </summary>
	public readonly ClientOptions Options;

	/// <summary>
	/// Constructs a new <see cref="TcpTestClient"/>
	/// </summary>
	/// <param name="options"></param>
	/// <param name="interactiveMode"></param>
	/// <param name="log"></param>
	public TcpTestClient(ClientOptions options, bool interactiveMode, ILogger log) {
		_interactiveMode = interactiveMode;
		_log = log;
		_useSsl = options.UseTls;
		TcpEndpoint = new DnsEndPoint(options.Host, options.TcpPort);
		_validateServer = options.TlsValidateServer;
		Options = options;
	}

	/// <summary>
	/// Creates a new raw TCP connection
	/// </summary>
	/// <param name="context"></param>
	/// <param name="handlePackage"></param>
	/// <param name="connectionEstablished"></param>
	/// <param name="connectionClosed"></param>
	/// <param name="failContextOnError"></param>
	/// <param name="tcpEndPoint"></param>
	/// <returns></returns>
	/// <exception cref="Exception"></exception>
	public Connection CreateTcpConnection(CommandProcessorContext context,
		Action<Connection, TcpPackage> handlePackage,
		Action<Connection> connectionEstablished = null,
		Action<Connection, SocketError> connectionClosed = null,
		bool failContextOnError = true,
		IPEndPoint tcpEndPoint = null) {
		var connectionCreatedEvent = new ManualResetEventSlim(false);
		Connection typedConnection = null;

		Action<ITcpConnection> onConnectionEstablished = conn => {
			// we execute callback on ThreadPool because on FreeBSD it can be called synchronously
			// causing deadlock
			ThreadPool.QueueUserWorkItem(_ => {
				if (!_interactiveMode)
					_log.Information(
						"TcpTypedConnection: connected to [{remoteEndPoint}, L{localEndPoint}, {connectionId:B}].",
						conn.RemoteEndPoint, conn.LocalEndPoint, conn.ConnectionId);
				if (connectionEstablished != null) {
					if (!connectionCreatedEvent.Wait(10000))
						throw new Exception("TcpTypedConnection: creation took too long!");
					connectionEstablished(typedConnection);
				}
			});
		};
		Action<ITcpConnection, SocketError> onConnectionFailed = (conn, error) => {
			_log.Error(
				"TcpTypedConnection: connection to [{remoteEndPoint}, L{localEndPoint}, {connectionId:B}] failed. Error: {e}.",
				conn.RemoteEndPoint, conn.LocalEndPoint, conn.ConnectionId, error);

			if (connectionClosed != null)
				connectionClosed(null, error);

			if (failContextOnError)
				context.Fail(reason: string.Format("Socket connection failed with error {0}.", error));
		};

		var endpoint = tcpEndPoint ?? TcpEndpoint;
		ITcpConnection connection;
		if (_useSsl) {
			connection = _connector.ConnectSslTo(
				Guid.NewGuid(),
				endpoint.GetHost(),
				endpoint.GetOtherNames(),
				endpoint.ResolveDnsToIPAddress(),
				TcpConnectionManager.ConnectionTimeout,
				(_, _, err, _) => (err == SslPolicyErrors.None || !_validateServer, err.ToString()),
				() => null,
				onConnectionEstablished,
				onConnectionFailed,
				verbose: false);
		} else {
			connection = _connector.ConnectTo(
				Guid.NewGuid(),
				endpoint.ResolveDnsToIPAddress(),
				TcpConnectionManager.ConnectionTimeout,
				onConnectionEstablished,
				onConnectionFailed,
				verbose: false);
		}

		typedConnection = new Connection(connection, new RawMessageFormatter(_bufferManager),
			new LengthPrefixMessageFramer());
		typedConnection.ConnectionClosed +=
			(conn, error) => {
				if (!_interactiveMode || error != SocketError.Success) {
					_log.Information(
						"TcpTypedConnection: connection [{remoteEndPoint}, L{localEndPoint}] was closed {status}",
						conn.RemoteEndPoint, conn.LocalEndPoint,
						error == SocketError.Success ? "cleanly." : "with error: " + error + ".");
				}

				if (connectionClosed != null)
					connectionClosed(conn, error);
				else
					_log.Information("connectionClosed callback was null");
			};
		connectionCreatedEvent.Set();

		typedConnection.ReceiveAsync(
			(conn, pkg) => {
				var package = new TcpPackage();
				bool validPackage = false;
				try {
					package = TcpPackage.FromArraySegment(new ArraySegment<byte>(pkg));
					validPackage = true;

					if (package.Command == TcpCommand.HeartbeatRequestCommand) {
						var resp = new TcpPackage(TcpCommand.HeartbeatResponseCommand, Guid.NewGuid(), null);
						conn.EnqueueSend(resp.AsByteArray());
						return;
					}

					handlePackage(conn, package);
				} catch (Exception ex) {
					_log.Information(ex,
						"TcpTypedConnection: [{remoteEndPoint}, L{localEndPoint}] ERROR for {package}. Connection will be closed.",
						conn.RemoteEndPoint, conn.LocalEndPoint,
						validPackage ? package.Command as object : "<invalid package>");
					conn.Close(ex.Message);

					if (failContextOnError)
						context.Fail(ex);
				}
			});

		return typedConnection;
	}
}
