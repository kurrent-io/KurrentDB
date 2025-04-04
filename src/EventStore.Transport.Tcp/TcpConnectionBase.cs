// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using EventStore.Common.Utils;

namespace EventStore.Transport.Tcp;

public class TcpConnectionBase : IMonitoredTcpConnection {
	public IPEndPoint RemoteEndPoint {
		get { return _remoteEndPoint; }
	}

	public IPEndPoint LocalEndPoint {
		get { return _localEndPoint; }
	}

	public bool IsInitialized {
		get { return _socket != null; }
	}

	public bool IsClosed {
		get { return _isClosed; }
	}

	public bool InSend {
		get { return Interlocked.Read(ref _lastSendStarted) >= 0; }
	}

	public bool InReceive {
		get { return Interlocked.Read(ref _lastReceiveStarted) >= 0; }
	}

	public int PendingSendBytes {
		get { return _pendingSendBytes; }
	}

	public int InSendBytes {
		get { return _inSendBytes; }
	}

	public int PendingReceivedBytes {
		get { return _pendingReceivedBytes; }
	}

	public long TotalBytesSent {
		get { return Interlocked.Read(ref _totalBytesSent); }
	}

	public long TotalBytesReceived {
		get { return Interlocked.Read(ref _totalBytesReceived); }
	}

	public int SendCalls {
		get { return _sentAsyncs; }
	}

	public int SendCallbacks {
		get { return _sentAsyncCallbacks; }
	}

	public int ReceiveCalls {
		get { return _recvAsyncs; }
	}

	public int ReceiveCallbacks {
		get { return _recvAsyncCallbacks; }
	}

	public bool IsReadyForSend {
		get {
			try {
				return !_isClosed && _socket.Poll(0, SelectMode.SelectWrite);
			} catch (ObjectDisposedException) {
				//TODO: why do we get this?
				return false;
			}
		}
	}

	public bool IsReadyForReceive {
		get {
			try {
				return !_isClosed && _socket.Poll(0, SelectMode.SelectRead);
			} catch (ObjectDisposedException) {
				//TODO: why do we get this?
				return false;
			}
		}
	}

	public bool IsFaulted {
		get {
			try {
				return !_isClosed && _socket.Poll(0, SelectMode.SelectError);
			} catch (ObjectDisposedException) {
				//TODO: why do we get this?
				return false;
			}
		}
	}

	public DateTime? LastSendStarted {
		get {
			var ticks = Interlocked.Read(ref _lastSendStarted);
			return ticks >= 0 ? new DateTime(ticks) : (DateTime?)null;
		}
	}

	public DateTime? LastReceiveStarted {
		get {
			var ticks = Interlocked.Read(ref _lastReceiveStarted);
			return ticks >= 0 ? new DateTime(ticks) : (DateTime?)null;
		}
	}

	private Socket _socket;
	protected readonly IPEndPoint _remoteEndPoint;
	private IPEndPoint _localEndPoint;

	private long _lastSendStarted = -1;
	private long _lastReceiveStarted = -1;
	private bool _isClosed;

	private int _pendingSendBytes;
	private int _inSendBytes;
	private int _pendingReceivedBytes;
	private long _totalBytesSent;
	private long _totalBytesReceived;

	private int _sentAsyncs;
	private int _sentAsyncCallbacks;
	private int _recvAsyncs;
	private int _recvAsyncCallbacks;

	public TcpConnectionBase(IPEndPoint remoteEndPoint) {
		Ensure.NotNull(remoteEndPoint, "remoteEndPoint");
		_remoteEndPoint = remoteEndPoint;

		TcpConnectionMonitor.Default.Register(this);
	}

	protected void InitConnectionBase(Socket socket) {
		Ensure.NotNull(socket, "socket");

		_socket = socket;
		_localEndPoint = Helper.EatException(() => (IPEndPoint)socket.LocalEndPoint);
	}

	protected void NotifySendScheduled(int bytes) {
		Interlocked.Add(ref _pendingSendBytes, bytes);
	}

	protected void NotifySendStarting(int bytes) {
		if (Interlocked.CompareExchange(ref _lastSendStarted, DateTime.UtcNow.Ticks, -1) != -1)
			throw new Exception("Concurrent send detected.");
		Interlocked.Add(ref _pendingSendBytes, -bytes);
		Interlocked.Add(ref _inSendBytes, bytes);
		Interlocked.Increment(ref _sentAsyncs);
	}

	protected void NotifySendCompleted(int bytes) {
		Interlocked.Exchange(ref _lastSendStarted, -1);
		Interlocked.Add(ref _inSendBytes, -bytes);
		Interlocked.Add(ref _totalBytesSent, bytes);
		Interlocked.Increment(ref _sentAsyncCallbacks);
	}

	protected void NotifyReceiveStarting() {
		if (Interlocked.CompareExchange(ref _lastReceiveStarted, DateTime.UtcNow.Ticks, -1) != -1)
			throw new Exception("Concurrent receive detected.");

		Interlocked.Increment(ref _recvAsyncs);
	}

	protected void NotifyReceiveCompleted(int bytes) {
		Interlocked.Exchange(ref _lastReceiveStarted, -1);
		Interlocked.Add(ref _pendingReceivedBytes, bytes);
		Interlocked.Add(ref _totalBytesReceived, bytes);
		Interlocked.Increment(ref _recvAsyncCallbacks);
	}

	protected void NotifyReceiveDispatched(int bytes) {
		Interlocked.Add(ref _pendingReceivedBytes, -bytes);
	}

	protected void NotifyClosed() {
		_isClosed = true;
		TcpConnectionMonitor.Default.Unregister(this);
	}
}
