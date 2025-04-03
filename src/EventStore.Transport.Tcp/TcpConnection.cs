// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using EventStore.BufferManagement;
using EventStore.Common.Utils;
using System.Collections.Concurrent;
using ILogger = Serilog.ILogger;

namespace EventStore.Transport.Tcp;

public class TcpConnection : TcpConnectionBase, ITcpConnection {
	internal const int MaxSendPacketSize = 65535 /*Max IP packet size*/ - 20 /*IP packet header size*/ - 32 /*TCP min header size*/;

	internal static readonly BufferManager BufferManager =
		new BufferManager(TcpConfiguration.BufferChunksCount, TcpConfiguration.SocketBufferSize);

	private static readonly ILogger Log = Serilog.Log.ForContext<TcpConnection>();

	private static readonly SocketArgsPool SocketArgsPool = new SocketArgsPool("TcpConnection.SocketArgsPool",
		TcpConfiguration.SendReceivePoolSize,
		() => new SocketAsyncEventArgs());

	public static ITcpConnection CreateConnectingTcpConnection(Guid connectionId,
		IPEndPoint remoteEndPoint,
		TcpClientConnector connector,
		TimeSpan connectionTimeout,
		Action<ITcpConnection> onConnectionEstablished,
		Action<ITcpConnection, SocketError> onConnectionFailed,
		bool verbose) {
		var connection = new TcpConnection(connectionId, remoteEndPoint, verbose);
// ReSharper disable ImplicitlyCapturedClosure
		connector.InitConnect(remoteEndPoint,
			(socket) => {
				connection.InitSocket(socket);
			},
			(_, socket) => {
				connection.InitSendReceive();
				if (onConnectionEstablished != null)
					onConnectionEstablished(connection);
			},
			(_, socketError) => {
				if (onConnectionFailed != null)
					onConnectionFailed(connection, socketError);
			}, connection, connectionTimeout);
// ReSharper restore ImplicitlyCapturedClosure
		return connection;
	}

	public static ITcpConnection CreateAcceptedTcpConnection(Guid connectionId, IPEndPoint remoteEndPoint,
		Socket socket, bool verbose) {
		var connection = new TcpConnection(connectionId, remoteEndPoint, verbose);
		connection.InitSocket(socket);
		connection.InitSendReceive();
		return connection;
	}

	public event Action<ITcpConnection, SocketError> ConnectionClosed;

	public Guid ConnectionId {
		get { return _connectionId; }
	}

	public int SendQueueSize {
		get { return _sendQueue.Count; }
	}

	public string ClientConnectionName {
		get { return _clientConnectionName; }
	}

	private readonly Guid _connectionId;
	private readonly bool _verbose;
	private string _clientConnectionName;

	private Socket _socket;
	private SocketAsyncEventArgs _receiveSocketArgs;
	private SocketAsyncEventArgs _sendSocketArgs;

	private readonly ConcurrentQueueWrapper<ArraySegment<byte>> _sendQueue =
		new ConcurrentQueueWrapper<ArraySegment<byte>>();

	private readonly Queue<ReceivedData> _receiveQueue = new Queue<ReceivedData>();
	private readonly MemoryStream _memoryStream = new MemoryStream();
	private long _memoryStreamOffset = 0L;

	private readonly object _receivingLock = new object();
	private readonly object _sendLock = new object();
	private readonly object _closeLock = new object();
	private bool _isSending;
	private volatile bool _isClosed;
	private volatile bool _isClosing;

	private Action<ITcpConnection, IEnumerable<ArraySegment<byte>>> _receiveCallback;

	private TcpConnection(Guid connectionId, IPEndPoint remoteEndPoint, bool verbose) : base(remoteEndPoint) {
		Ensure.NotEmptyGuid(connectionId, "connectionId");

		_connectionId = connectionId;
		_verbose = verbose;
	}

	private void InitSocket(Socket socket) {
		_socket = socket;
	}

	private void InitSendReceive() {
		InitConnectionBase(_socket);
		lock (_sendLock) {
			try {
				_socket.NoDelay = true;
			} catch (ObjectDisposedException) {
				CloseInternal(SocketError.Shutdown, "Socket disposed.");
				return;
			} catch (SocketException) {
				CloseInternal(SocketError.Shutdown, "Socket is disposed.");
				return;
			}

			var receiveSocketArgs = SocketArgsPool.Get();
			_receiveSocketArgs = receiveSocketArgs;
			_receiveSocketArgs.AcceptSocket = _socket;
			_receiveSocketArgs.Completed += OnReceiveAsyncCompleted;

			var sendSocketArgs = SocketArgsPool.Get();
			_sendSocketArgs = sendSocketArgs;
			_sendSocketArgs.AcceptSocket = _socket;
			_sendSocketArgs.Completed += OnSendAsyncCompleted;
		}

		StartReceive();
		TrySend();
	}

	public void EnqueueSend(IEnumerable<ArraySegment<byte>> data) {
		lock (_sendLock) {
			int bytes = 0;
			foreach (var segment in data) {
				_sendQueue.Enqueue(segment);
				bytes += segment.Count;
			}

			NotifySendScheduled(bytes);
		}

		TrySend();
	}

	private void TrySend() {
		bool continueSendSynchronously = true;
		try {
			do {
				lock (_sendLock) {
					if (_isSending || (_sendQueue.IsEmpty && _memoryStreamOffset >= _memoryStream.Length) || _sendSocketArgs == null) return;
					if (TcpConnectionMonitor.Default.IsSendBlocked()) return;
					_isSending = true;
				}

				if (_memoryStreamOffset >= _memoryStream.Length) {
					_memoryStream.SetLength(0);
					_memoryStreamOffset = 0L;

					ArraySegment<byte> sendPiece;
					while (_sendQueue.TryDequeue(out sendPiece)) {
						_memoryStream.Write(sendPiece.Array, sendPiece.Offset, sendPiece.Count);
						if (_memoryStream.Length >= MaxSendPacketSize)
							break;
					}
				}

				int sendingBytes = Math.Min((int)_memoryStream.Length - (int) _memoryStreamOffset, MaxSendPacketSize);

				_sendSocketArgs.SetBuffer(_memoryStream.GetBuffer(), (int) _memoryStreamOffset, sendingBytes);
				_memoryStreamOffset += sendingBytes;

				NotifySendStarting(_sendSocketArgs.Count);
				var firedAsync = _sendSocketArgs.AcceptSocket.SendAsync(_sendSocketArgs);
				if (firedAsync) {
					continueSendSynchronously = false;
				} else {
					continueSendSynchronously = ProcessSend(_sendSocketArgs);
				}
			} while (continueSendSynchronously);
		} catch (ObjectDisposedException) {
			ReturnSendingSocketArgs();
		}
	}

	private void OnSendAsyncCompleted(object sender, SocketAsyncEventArgs e) {
		if (ProcessSend(e)) {
			TrySend();
		}
	}

	private bool ProcessSend(SocketAsyncEventArgs socketArgs) {
		if (socketArgs.SocketError != SocketError.Success) {
			NotifySendCompleted(0);
			ReturnSendingSocketArgs();
			CloseInternal(socketArgs.SocketError, "Socket send error.");
			return false;
		} else {
			NotifySendCompleted(socketArgs.Count);

			if (_isClosed) {
				ReturnSendingSocketArgs();
				return false;
			} else {
				lock (_sendLock) {
					_isSending = false;
				}

				return true;
			}
		}
	}

	public void ReceiveAsync(Action<ITcpConnection, IEnumerable<ArraySegment<byte>>> callback) {
		if (callback == null)
			throw new ArgumentNullException("callback");

		lock (_receivingLock) {
			if (_receiveCallback != null) {
				Log.Fatal("ReceiveAsync called again while previous call was not fulfilled");
				throw new InvalidOperationException(
					"ReceiveAsync called again while previous call was not fulfilled");
			}

			_receiveCallback = callback;
		}

		TryDequeueReceivedData();
	}

	private void StartReceive() {
		try {
			bool continueReceiveSynchronously = true;

			do {
				var buffer = BufferManager.CheckOut();
				if (buffer.Array == null || buffer.Count == 0 || buffer.Array.Length < buffer.Offset + buffer.Count)
					throw new Exception("Invalid buffer allocated");
				// TODO AN: do we need to lock on _receiveSocketArgs?..
				lock (_receiveSocketArgs) {
					_receiveSocketArgs.SetBuffer(buffer.Array, buffer.Offset, buffer.Count);
					if (_receiveSocketArgs.Buffer == null) throw new Exception("Buffer was not set");
				}

				NotifyReceiveStarting();
				bool firedAsync;
				lock (_receiveSocketArgs) {
					if (_receiveSocketArgs.Buffer == null) throw new Exception("Buffer was lost");
					firedAsync = _receiveSocketArgs.AcceptSocket.ReceiveAsync(_receiveSocketArgs);
				}

				if (firedAsync) {
					continueReceiveSynchronously = false;
				} else {
					var processReceiveSuccess = ProcessReceive(_receiveSocketArgs);
					if (processReceiveSuccess) {
						TryDequeueReceivedData();
					}

					continueReceiveSynchronously = processReceiveSuccess;
				}
			} while (continueReceiveSynchronously);
		} catch (ObjectDisposedException) {
			ReturnReceivingSocketArgs();
		}
	}

	private void OnReceiveAsyncCompleted(object sender, SocketAsyncEventArgs e) {
		if (ProcessReceive(e)) {
			TryDequeueReceivedData();
			StartReceive();
		}
	}

	private bool ProcessReceive(SocketAsyncEventArgs socketArgs) {
		// socket closed normally or some error occurred
		if (socketArgs.BytesTransferred == 0 || socketArgs.SocketError != SocketError.Success) {
			NotifyReceiveCompleted(0);
			ReturnReceivingSocketArgs();
			CloseInternal(socketArgs.SocketError,
				socketArgs.SocketError != SocketError.Success ? "Socket receive error" : "Socket closed");
			return false;
		}

		NotifyReceiveCompleted(socketArgs.BytesTransferred);

		lock (_receivingLock) {
			var buf = new ArraySegment<byte>(socketArgs.Buffer, socketArgs.Offset, socketArgs.Count);
			_receiveQueue.Enqueue(new ReceivedData(buf, socketArgs.BytesTransferred));
		}

		lock (_receiveSocketArgs) {
			if (socketArgs.Buffer == null)
				throw new Exception("Cleaning already null buffer");
			socketArgs.SetBuffer(null, 0, 0);
		}

		return true;
	}

	private void TryDequeueReceivedData() {
		Action<ITcpConnection, IEnumerable<ArraySegment<byte>>> callback;
		List<ReceivedData> res;
		lock (_receivingLock) {
			// no awaiting callback or no data to dequeue
			if (_receiveCallback == null || _receiveQueue.Count == 0)
				return;

			res = new List<ReceivedData>(_receiveQueue.Count);
			while (_receiveQueue.Count > 0) {
				res.Add(_receiveQueue.Dequeue());
			}

			callback = _receiveCallback;
			_receiveCallback = null;
		}

		var data = new ArraySegment<byte>[res.Count];
		int bytes = 0;
		for (int i = 0; i < data.Length; ++i) {
			var d = res[i];
			bytes += d.DataLen;
			data[i] = new ArraySegment<byte>(d.Buf.Array, d.Buf.Offset, d.DataLen);
		}

		lock (_closeLock) {
			if(!_isClosed)
				callback(this, data);
		}

		for (int i = 0, n = res.Count; i < n; ++i) {
			BufferManager.CheckIn(res[i].Buf); // dispose buffers
		}

		NotifyReceiveDispatched(bytes);
	}

	public void Close(string reason) {
		CloseInternal(SocketError.Success, reason ?? "Normal socket close."); // normal socket closing
	}

	private void CloseInternal(SocketError socketError, string reason) {
		lock (_closeLock) {
			if (_isClosing) return;
			_isClosing = true;
		}

		if (_socket != null) {
			Helper.EatException(() => _socket.Shutdown(SocketShutdown.Both));
			Helper.EatException(() => _socket.Close());
		}

		lock (_closeLock) {
			_isClosed = true;
		}

		NotifyClosed();

		if (_verbose) {
			Log.Information(
				"DB {connectionType} closed [{dateTime:HH:mm:ss.fff}: N{remoteEndPoint}, L{localEndPoint}, {connectionId:B}]:Received bytes: {totalBytesReceived}, Sent bytes: {totalBytesSent}",
				GetType().Name, DateTime.UtcNow, RemoteEndPoint, LocalEndPoint, _connectionId,
				TotalBytesReceived, TotalBytesSent);
			Log.Information(
				"DB {connectionType} closed [{dateTime:HH:mm:ss.fff}: N{remoteEndPoint}, L{localEndPoint}, {connectionId:B}]:Send calls: {sendCalls}, callbacks: {sendCallbacks}",
				GetType().Name, DateTime.UtcNow, RemoteEndPoint, LocalEndPoint, _connectionId,
				SendCalls, SendCallbacks);
			Log.Information(
				"DB {connectionType} closed [{dateTime:HH:mm:ss.fff}: N{remoteEndPoint}, L{localEndPoint}, {connectionId:B}]:Receive calls: {receiveCalls}, callbacks: {receiveCallbacks}",
				GetType().Name, DateTime.UtcNow, RemoteEndPoint, LocalEndPoint, _connectionId,
				ReceiveCalls, ReceiveCallbacks);
			Log.Information(
				"DB {connectionType} closed [{dateTime:HH:mm:ss.fff}: N{remoteEndPoint}, L{localEndPoint}, {connectionId:B}]:Close reason: [{socketError}] {reason}",
				GetType().Name, DateTime.UtcNow, RemoteEndPoint, LocalEndPoint, _connectionId,
				socketError, reason);
		}

		lock (_sendLock) {
			if (!_isSending)
				ReturnSendingSocketArgs();
		}

		var handler = ConnectionClosed;
		if (handler != null)
			handler(this, socketError);
	}

	private void ReturnSendingSocketArgs() {
		var socketArgs = Interlocked.Exchange(ref _sendSocketArgs, null);
		if (socketArgs != null) {
			socketArgs.Completed -= OnSendAsyncCompleted;
			socketArgs.AcceptSocket = null;
			if (socketArgs.Buffer != null)
				socketArgs.SetBuffer(null, 0, 0);
			SocketArgsPool.Return(socketArgs);
		}
	}

	private void ReturnReceivingSocketArgs() {
		var socketArgs = Interlocked.Exchange(ref _receiveSocketArgs, null);
		if (socketArgs != null) {
			socketArgs.Completed -= OnReceiveAsyncCompleted;
			socketArgs.AcceptSocket = null;
			if (socketArgs.Buffer != null) {
				BufferManager.CheckIn(
					new ArraySegment<byte>(socketArgs.Buffer, socketArgs.Offset, socketArgs.Count));
				socketArgs.SetBuffer(null, 0, 0);
			}

			SocketArgsPool.Return(socketArgs);
		}
	}

	public void SetClientConnectionName(string clientConnectionName) {
		_clientConnectionName = clientConnectionName;
	}

	public override string ToString() {
		return RemoteEndPoint.ToString();
	}

	private struct ReceivedData {
		public readonly ArraySegment<byte> Buf;
		public readonly int DataLen;

		public ReceivedData(ArraySegment<byte> buf, int dataLen) {
			Buf = buf;
			DataLen = dataLen;
		}
	}
}
