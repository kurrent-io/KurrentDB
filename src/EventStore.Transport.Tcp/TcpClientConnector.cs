// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using EventStore.Common.Utils;
using ILogger = Serilog.ILogger;

namespace EventStore.Transport.Tcp;

public class TcpClientConnector {
	private const int CheckPeriodMs = 200;

	private readonly SocketArgsPool _connectSocketArgsPool;
	private readonly ConcurrentDictionary<Guid, PendingConnection> _pendingConections;
	private readonly Timer _timer;

	private static readonly ILogger Log = Serilog.Log.ForContext<TcpClientConnector>();

	public TcpClientConnector() {
		_connectSocketArgsPool = new SocketArgsPool("TcpClientConnector._connectSocketArgsPool",
			TcpConfiguration.ConnectPoolSize,
			CreateConnectSocketArgs);
		_pendingConections = new ConcurrentDictionary<Guid, PendingConnection>();
		_timer = new Timer(TimerCallback);
		// prevent possible null reference exceptions in case of slow initialization
		_timer.Change(CheckPeriodMs, Timeout.Infinite);
	}

	private SocketAsyncEventArgs CreateConnectSocketArgs() {
		var socketArgs = new SocketAsyncEventArgs();
		socketArgs.Completed += ConnectCompleted;
		socketArgs.UserToken = new CallbacksStateToken();
		return socketArgs;
	}

	public ITcpConnection ConnectTo(Guid connectionId,
		IPEndPoint remoteEndPoint,
		TimeSpan connectionTimeout,
		Action<ITcpConnection> onConnectionEstablished = null,
		Action<ITcpConnection, SocketError> onConnectionFailed = null,
		bool verbose = true) {
		Ensure.NotNull(remoteEndPoint, "remoteEndPoint");
		return TcpConnection.CreateConnectingTcpConnection(connectionId, remoteEndPoint, this, connectionTimeout,
			onConnectionEstablished, onConnectionFailed, verbose);
	}

	public ITcpConnection ConnectSslTo(Guid connectionId,
		string targetHost,
		string[] otherNames,
		IPEndPoint remoteEndPoint,
		TimeSpan connectionTimeout,
		CertificateDelegates.ServerCertificateValidator sslServerCertValidator,
		Func<X509CertificateCollection> clientCertificatesSelector,
		Action<ITcpConnection> onConnectionEstablished = null,
		Action<ITcpConnection, SocketError> onConnectionFailed = null,
		bool verbose = true) {
		Ensure.NotNull(remoteEndPoint, "remoteEndPoint");
		return TcpConnectionSsl.CreateConnectingConnection(connectionId, targetHost, otherNames, remoteEndPoint, sslServerCertValidator,
			clientCertificatesSelector, this, connectionTimeout, onConnectionEstablished, onConnectionFailed, verbose);
	}

	internal void InitConnect(IPEndPoint serverEndPoint,
		Action<Socket> onSocketAssigned,
		Action<IPEndPoint, Socket> onConnectionEstablished,
		Action<IPEndPoint, SocketError> onConnectionFailed,
		ITcpConnection connection,
		TimeSpan connectionTimeout) {
		if (serverEndPoint == null)
			throw new ArgumentNullException("serverEndPoint");
		if (onSocketAssigned == null)
			throw new ArgumentNullException("onSocketAssigned");
		if (onConnectionEstablished == null)
			throw new ArgumentNullException("onConnectionEstablished");
		if (onConnectionFailed == null)
			throw new ArgumentNullException("onConnectionFailed");

		var socketArgs = _connectSocketArgsPool.Get();
		var connectingSocket = new Socket(serverEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
		onSocketAssigned(connectingSocket);
		socketArgs.RemoteEndPoint = serverEndPoint;
		socketArgs.AcceptSocket = connectingSocket;
		var callbacks = (CallbacksStateToken)socketArgs.UserToken;
		callbacks.OnConnectionEstablished = onConnectionEstablished;
		callbacks.OnConnectionFailed = onConnectionFailed;
		callbacks.PendingConnection = new PendingConnection(connection, DateTime.UtcNow.Add(connectionTimeout));

		AddToConnecting(callbacks.PendingConnection);

		try {
			var firedAsync = connectingSocket.ConnectAsync(socketArgs);
			if (!firedAsync)
				ProcessConnect(socketArgs);
		} catch (ObjectDisposedException) {
			HandleBadConnect(socketArgs);
		}
	}

	private void ConnectCompleted(object sender, SocketAsyncEventArgs e) {
		ProcessConnect(e);
	}

	private void ProcessConnect(SocketAsyncEventArgs e) {
		if (e.SocketError != SocketError.Success)
			HandleBadConnect(e);
		else
			OnSocketConnected(e);
	}

	private void HandleBadConnect(SocketAsyncEventArgs socketArgs) {
		var serverEndPoint = socketArgs.RemoteEndPoint;
		var socketError = socketArgs.SocketError;
		var callbacks = (CallbacksStateToken)socketArgs.UserToken;
		var onConnectionFailed = callbacks.OnConnectionFailed;
		var pendingConnection = callbacks.PendingConnection;

		Helper.EatException(() => socketArgs.AcceptSocket.Close());
		socketArgs.AcceptSocket = null;
		callbacks.Reset();
		_connectSocketArgsPool.Return(socketArgs);

		if (RemoveFromConnecting(pendingConnection))
			onConnectionFailed((IPEndPoint)serverEndPoint, socketError);
	}

	private void OnSocketConnected(SocketAsyncEventArgs socketArgs) {
		var remoteEndPoint = (IPEndPoint)socketArgs.RemoteEndPoint;
		var socket = socketArgs.AcceptSocket;
		var callbacks = (CallbacksStateToken)socketArgs.UserToken;
		var onConnectionEstablished = callbacks.OnConnectionEstablished;
		var pendingConnection = callbacks.PendingConnection;

		socketArgs.AcceptSocket = null;
		callbacks.Reset();
		_connectSocketArgsPool.Return(socketArgs);

		if (RemoveFromConnecting(pendingConnection))
			onConnectionEstablished(remoteEndPoint, socket);
	}

	private void TimerCallback(object state) {
		foreach (var pendingConnection in _pendingConections.Values) {
			if (DateTime.UtcNow >= pendingConnection.WhenToKill && RemoveFromConnecting(pendingConnection))
				Helper.EatException(() => pendingConnection.Connection.Close("Connection establishment timeout."));
		}

		try {
			_timer.Change(CheckPeriodMs, Timeout.Infinite);
		} catch (ObjectDisposedException) {
			// ignore
		}
	}

	private void AddToConnecting(PendingConnection pendingConnection) {
		_pendingConections.TryAdd(pendingConnection.Connection.ConnectionId, pendingConnection);
	}

	private bool RemoveFromConnecting(PendingConnection pendingConnection) {
		PendingConnection conn;
		if (pendingConnection.Connection == null) {
			Log.Warning("Network Card disconnected");
			return false;
		}

		return _pendingConections.TryRemove(pendingConnection.Connection.ConnectionId, out conn)
		       && Interlocked.CompareExchange(ref conn.Done, 1, 0) == 0;
	}

	private class CallbacksStateToken {
		public Action<IPEndPoint, Socket> OnConnectionEstablished;
		public Action<IPEndPoint, SocketError> OnConnectionFailed;
		public PendingConnection PendingConnection;

		public void Reset() {
			OnConnectionEstablished = null;
			OnConnectionFailed = null;
			PendingConnection = null;
		}
	}

	private class PendingConnection {
		public readonly ITcpConnection Connection;
		public readonly DateTime WhenToKill;
		public int Done;

		public PendingConnection(ITcpConnection connection, DateTime whenToKill) {
			Connection = connection;
			WhenToKill = whenToKill;
		}
	}
}
