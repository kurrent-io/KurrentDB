// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Common.Utils;
using ILogger = Serilog.ILogger;

namespace EventStore.Transport.Tcp;

public class TcpConnectionSsl : TcpConnectionBase, ITcpConnection {
	private static readonly ILogger Log = Serilog.Log.ForContext<TcpConnectionSsl>();

	public static ITcpConnection CreateConnectingConnection(Guid connectionId,
		string targetHost,
		string[] otherNames,
		IPEndPoint remoteEndPoint,
		CertificateDelegates.ServerCertificateValidator serverCertValidator,
		Func<X509CertificateCollection> clientCertificatesSelector,
		TcpClientConnector connector,
		TimeSpan connectionTimeout,
		Action<ITcpConnection> onConnectionEstablished,
		Action<ITcpConnection, SocketError> onConnectionFailed,
		bool verbose) {
		var connection = new TcpConnectionSsl(connectionId, remoteEndPoint, verbose);
		// ReSharper disable ImplicitlyCapturedClosure
		connector.InitConnect(remoteEndPoint,
			(socket) => {
				connection.InitClientSocket(socket);
			},
			(_, socket) => {
				connection.InitSslStream(targetHost, otherNames, serverCertValidator, clientCertificatesSelector, verbose);
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

	public static ITcpConnection CreateServerFromSocket(Guid connectionId,
		IPEndPoint remoteEndPoint,
		Socket socket,
		Func<X509Certificate2> serverCertificateSelector,
		Func<X509Certificate2Collection> intermediatesSelector,
		CertificateDelegates.ClientCertificateValidator clientCertValidator,
		bool verbose) {
		var connection = new TcpConnectionSsl(connectionId, remoteEndPoint, verbose);
		connection.InitServerSocket(socket, serverCertificateSelector, intermediatesSelector, clientCertValidator, verbose);
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
	public string _clientConnectionName;

	private Socket _socket;

	private readonly ConcurrentQueueWrapper<ArraySegment<byte>> _sendQueue =
		new ConcurrentQueueWrapper<ArraySegment<byte>>();

	private readonly ConcurrentQueueWrapper<ReceivedData>
		_receiveQueue = new ConcurrentQueueWrapper<ReceivedData>();

	private readonly MemoryStream _memoryStream = new MemoryStream();
	private long _memoryStreamOffset = 0L;

	private readonly object _streamLock = new object();
	private readonly object _closeLock = new object();
	private bool _isSending;
	private int _receiveHandling;
	private volatile bool _isClosed;
	private volatile bool _isClosing;

	private Action<ITcpConnection, IEnumerable<ArraySegment<byte>>> _receiveCallback;

	private SslStream _sslStream;
	private bool _isAuthenticated;
	private int _sendingBytes;
	private CertificateDelegates.ServerCertificateValidator _serverCertValidator;
	private CertificateDelegates.ClientCertificateValidator _clientCertValidator;
	private string[] _otherNames;
	private readonly byte[] _receiveBuffer = new byte[TcpConnection.BufferManager.ChunkSize];

	private TcpConnectionSsl(Guid connectionId, IPEndPoint remoteEndPoint, bool verbose) : base(remoteEndPoint) {
		Ensure.NotEmptyGuid(connectionId, "connectionId");

		_connectionId = connectionId;
		_verbose = verbose;
	}

	private void InitServerSocket(
		Socket socket,
		Func<X509Certificate2> serverCertificateSelector,
		Func<X509Certificate2Collection> intermediatesSelector,
		CertificateDelegates.ClientCertificateValidator clientCertValidator,
		bool verbose) {
		InitConnectionBase(socket);
		if (verbose)
			Console.WriteLine("TcpConnectionSsl::InitClientSocket({0}, L{1})", RemoteEndPoint, LocalEndPoint);

		_clientCertValidator = clientCertValidator;

		lock (_streamLock) {
			try {
				socket.NoDelay = true;
			} catch (ObjectDisposedException) {
				CloseInternal(SocketError.Shutdown, "Socket is disposed.");
				return;
			} catch (SocketException) {
				CloseInternal(SocketError.Shutdown, "Socket is disposed.");
				return;
			}

			try {
				_sslStream = new SslStream(new NetworkStream(socket, true), false);
			} catch (IOException exc) {
				Log.Debug(exc, "[S{remoteEndPoint}, L{localEndPoint}]: IOException on NetworkStream. The socket has already been disposed.", RemoteEndPoint,
					LocalEndPoint);
				return;
			}

			Task.Run(async () => await AuthenticateAsServerAsync(serverCertificateSelector, intermediatesSelector));
		}
	}

	private async Task AuthenticateAsServerAsync(
		Func<X509Certificate2> serverCertificateSelector,
		Func<X509Certificate2Collection> intermediatesSelector) {
		try {
			var enabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;
			var certificate = serverCertificateSelector?.Invoke();
			var intermediates = intermediatesSelector?.Invoke();
			Ensure.NotNull(certificate, "certificate");

			await _sslStream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions {
				ServerCertificateContext = SslStreamCertificateContext.Create(
					certificate!, intermediates, offline: true),
				ClientCertificateRequired = true,
				EnabledSslProtocols = enabledSslProtocols,
				CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
				RemoteCertificateValidationCallback = ValidateClientCertificate,
				ApplicationProtocols = new List<SslApplicationProtocol>(),
				AllowRenegotiation = false,
			});

			lock (_streamLock) {
				if (_verbose)
					DisplaySslStreamInfo(_sslStream);
				_isAuthenticated = true;
			}
		} catch (AuthenticationException exc) {
			Log.Information(exc,
				"[S{remoteEndPoint}, L{localEndPoint}]: Authentication exception on AuthenticateAsServerAsync.",
				RemoteEndPoint, LocalEndPoint);
			CloseInternal(SocketError.SocketError, exc.Message);
		} catch (ObjectDisposedException) {
			CloseInternal(SocketError.SocketError, "SslStream disposed.");
		} catch (Exception exc) {
			Log.Information(exc,
				"[S{remoteEndPoint}, L{localEndPoint}]: Exception on AuthenticateAsServerAsync.",
				RemoteEndPoint, LocalEndPoint);
			CloseInternal(SocketError.SocketError, exc.Message);
		}

		StartReceive();
		TrySend();
	}

	private void InitClientSocket(Socket socket) {
		_socket = socket;
	}

	private void InitSslStream(string targetHost, string[] otherNames, CertificateDelegates.ServerCertificateValidator serverCertValidator, Func<X509CertificateCollection> clientCertificatesSelector, bool verbose) {
		Ensure.NotNull(targetHost, "targetHost");
		InitConnectionBase(_socket);
		if (verbose)
			Console.WriteLine("TcpConnectionSsl::InitClientSslStream({0}, L{1})", RemoteEndPoint, LocalEndPoint);

		_serverCertValidator = serverCertValidator;
		_otherNames = otherNames;

		lock (_streamLock) {
			try {
				_socket.NoDelay = true;
			} catch (ObjectDisposedException) {
				CloseInternal(SocketError.Shutdown, "Socket is disposed.");
				return;
			} catch (SocketException) {
				CloseInternal(SocketError.Shutdown, "Socket is disposed.");
				return;
			}

			try {
				_sslStream = new SslStream(new NetworkStream(_socket, true), false, ValidateServerCertificate, null);
			} catch (IOException exc) {
				Log.Debug(exc, "[S{remoteEndPoint}, L{localEndPoint}]: IOException on NetworkStream. The socket has already been disposed.", RemoteEndPoint,
					LocalEndPoint);
				return;
			}

			try {
				var enabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;
				var clientCertificates = clientCertificatesSelector?.Invoke();
				_sslStream.BeginAuthenticateAsClient(targetHost, clientCertificates, enabledSslProtocols, false, OnEndAuthenticateAsClient, _sslStream);
			} catch (AuthenticationException exc) {
				Log.Information(exc,
					"[S{remoteEndPoint}, L{localEndPoint}]: Authentication exception on BeginAuthenticateAsClient.",
					RemoteEndPoint, LocalEndPoint);
				CloseInternal(SocketError.SocketError, exc.Message);
			} catch (ObjectDisposedException) {
				CloseInternal(SocketError.SocketError, "SslStream disposed.");
			} catch (Exception exc) {
				Log.Information(exc,
					"[S{remoteEndPoint}, {localEndPoint}]: Exception on BeginAuthenticateAsClient.", RemoteEndPoint,
					LocalEndPoint);
				CloseInternal(SocketError.SocketError, exc.Message);
			}
		}
	}

	private void OnEndAuthenticateAsClient(IAsyncResult ar) {
		try {
			lock (_streamLock) {
				var sslStream = (SslStream)ar.AsyncState;
				sslStream.EndAuthenticateAsClient(ar);
				if (_verbose)
					DisplaySslStreamInfo(sslStream);
				_isAuthenticated = true;
			}

			StartReceive();
			TrySend();
		} catch (AuthenticationException exc) {
			Log.Information(exc,
				"[S{remoteEndPoint}, L{localEndPoint}]: Authentication exception on EndAuthenticateAsClient.",
				RemoteEndPoint, LocalEndPoint);
			CloseInternal(SocketError.SocketError, exc.Message);
		} catch (ObjectDisposedException) {
			CloseInternal(SocketError.SocketError, "SslStream disposed.");
		} catch (Exception exc) {
			Log.Information(exc, "[S{remoteEndPoint}, L{localEndPoint}]: Exception on EndAuthenticateAsClient.",
				RemoteEndPoint, LocalEndPoint);
			CloseInternal(SocketError.SocketError, exc.Message);
		}
	}

	// The following method is invoked by the RemoteCertificateValidationDelegate.
	public bool ValidateServerCertificate(object sender, X509Certificate certificate, X509Chain chain,
		SslPolicyErrors sslPolicyErrors) {
		var (isValid, error) = _serverCertValidator(certificate, chain, sslPolicyErrors, _otherNames);
		if (!isValid && error != null) {
			Log.Error("Server certificate validation error: {e}", error);
		}
		return isValid;
	}

	public bool ValidateClientCertificate(object sender, X509Certificate certificate, X509Chain chain,
		SslPolicyErrors sslPolicyErrors) {
		var (isValid, error) = _clientCertValidator(certificate, chain, sslPolicyErrors);
		if (!isValid && error != null) {
			Log.Error("Client certificate validation error: {e}", error);
		}
		return isValid;
	}

	private void DisplaySslStreamInfo(SslStream stream) {
		Log.Information("[S{remoteEndPoint}, L{localEndPoint}]", RemoteEndPoint, LocalEndPoint);
		Log.Verbose("Cipher: {cipherAlgorithm}", stream.CipherAlgorithm);
		try {
			Log.Verbose("Cipher strength: {cipherStrength}", stream.CipherStrength);
		} catch (NotImplementedException) {
		}

		Log.Verbose("Hash: {hashAlgorithm}", stream.HashAlgorithm);
		try {
			Log.Verbose("Hash strength: {hashStrength}", stream.HashStrength);
		} catch (NotImplementedException) {
		}

		Log.Verbose("Key exchange: {keyExchangeAlgorithm}", stream.KeyExchangeAlgorithm);
		try {
			Log.Verbose("Key exchange strength: {keyExchangeStrength}", stream.KeyExchangeStrength);
		} catch (NotImplementedException) {
		}

		Log.Information("Protocol: {sslProtocol}", stream.SslProtocol);
		Log.Information("Is authenticated: {isAuthenticated} as server? {isServer}", stream.IsAuthenticated,
			stream.IsServer);
		Log.Information("IsSigned: {isSigned}", stream.IsSigned);
		Log.Information("Is Encrypted: {isEncrypted}", stream.IsEncrypted);
		Log.Information("Can read: {canRead}, write {canWrite}", stream.CanRead, stream.CanWrite);
		Log.Information("Can timeout: {canTimeout}", stream.CanTimeout);
		try {
			Log.Information("Certificate revocation list checked: {checkCertRevocationStatus}",
				stream.CheckCertRevocationStatus);
		} catch (NotImplementedException) {
		}

		X509Certificate localCert = stream.LocalCertificate;
		if (localCert != null)
			Log.Information(
				"Local certificate was issued to {subject} and is valid from {effectiveDate} until {expirationDate}.",
				localCert.Subject, localCert.GetEffectiveDateString(), localCert.GetExpirationDateString());
		else
			Log.Information("Local certificate is null.");

		// Display the properties of the client's certificate.
		X509Certificate remoteCert = stream.RemoteCertificate;
		if (remoteCert != null)
			Log.Information(
				"Remote certificate was issued to {subject} and is valid from {remoteCertEffectiveDate} until {remoteCertExpirationDate}.",
				remoteCert.Subject, remoteCert.GetEffectiveDateString(), remoteCert.GetExpirationDateString());
		else
			Log.Information("Remote certificate is null.");
	}

	public void EnqueueSend(IEnumerable<ArraySegment<byte>> data) {
		lock (_streamLock) {
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
				lock (_streamLock) {
					if (_isSending || (_sendQueue.IsEmpty && _memoryStreamOffset >= _memoryStream.Length) || _sslStream == null || !_isAuthenticated) return;
					if (TcpConnectionMonitor.Default.IsSendBlocked()) return;
					_isSending = true;
				}

				if (_memoryStreamOffset >= _memoryStream.Length) {
					_memoryStream.SetLength(0);
					_memoryStreamOffset = 0L;

					ArraySegment<byte> sendPiece;
					while (_sendQueue.TryDequeue(out sendPiece)) {
						_memoryStream.Write(sendPiece.Array, sendPiece.Offset, sendPiece.Count);
						if (_memoryStream.Length >= TcpConnection.MaxSendPacketSize)
							break;
					}
				}

				_sendingBytes = Math.Min((int)_memoryStream.Length - (int) _memoryStreamOffset, TcpConnection.MaxSendPacketSize);

				NotifySendStarting(_sendingBytes);
				var result = _sslStream.BeginWrite(_memoryStream.GetBuffer(), (int)_memoryStreamOffset, _sendingBytes, OnEndWrite, null);
				_memoryStreamOffset += _sendingBytes;
				continueSendSynchronously = result.CompletedSynchronously;
				if (continueSendSynchronously) {
					EndWrite(result);
				}
			} while (continueSendSynchronously);
		} catch (SocketException exc) {
			Log.Debug(exc, "SocketException '{e}' during BeginWrite.", exc.SocketErrorCode);
			CloseInternal(exc.SocketErrorCode, "SocketException during BeginWrite.");
		} catch (ObjectDisposedException) {
			CloseInternal(SocketError.SocketError, "SslStream disposed.");
		} catch (Exception exc) {
			Log.Debug(exc, "Exception during BeginWrite.");
			CloseInternal(SocketError.SocketError, "Exception during BeginWrite");
		}
	}

	private void OnEndWrite(IAsyncResult ar) {
		if (ar.CompletedSynchronously) return;

		EndWrite(ar);
		TrySend();
	}

	private void EndWrite(IAsyncResult ar) {
		try {
			_sslStream.EndWrite(ar);
			NotifySendCompleted(_sendingBytes);

			lock (_streamLock) {
				_isSending = false;
			}
		} catch (SocketException exc) {
			Log.Debug(exc, "SocketException '{e}' during EndWrite.", exc.SocketErrorCode);
			NotifySendCompleted(0);
			CloseInternal(exc.SocketErrorCode, "SocketException during EndWrite.");
		} catch (ObjectDisposedException) {
			NotifySendCompleted(0);
			CloseInternal(SocketError.SocketError, "SslStream disposed.");
		} catch (Exception exc) {
			Log.Debug(exc, "Exception during EndWrite.");
			NotifySendCompleted(0);
			CloseInternal(SocketError.SocketError, "Exception during EndWrite.");
		}
	}

	public void ReceiveAsync(Action<ITcpConnection, IEnumerable<ArraySegment<byte>>> callback) {
		Ensure.NotNull(callback, "callback");

		if (Interlocked.Exchange(ref _receiveCallback, callback) != null) {
			Log.Fatal("ReceiveAsync called again while previous call wasn't fulfilled");
			throw new InvalidOperationException("ReceiveAsync called again while previous call wasn't fulfilled");
		}

		TryDequeueReceivedData();
	}

	private void StartReceive() {
		try {
			bool continueReceiveSynchronously = true;

			do {
				NotifyReceiveStarting();
				var result = _sslStream.BeginRead(_receiveBuffer, 0, _receiveBuffer.Length, OnEndRead, null);
				continueReceiveSynchronously = result.CompletedSynchronously;
				if (continueReceiveSynchronously) {
					EndRead(result);
				}
			} while (continueReceiveSynchronously);
		} catch (SocketException exc) {
			Log.Debug(exc, "SocketException '{e}' during BeginRead.", exc.SocketErrorCode);
			CloseInternal(exc.SocketErrorCode, "SocketException during BeginRead.");
		} catch (ObjectDisposedException) {
			CloseInternal(SocketError.SocketError, "SslStream disposed.");
		} catch (Exception exc) {
			Log.Debug(exc, "Exception during BeginRead.");
			CloseInternal(SocketError.SocketError, "Exception during BeginRead.");
		}
	}

	private void OnEndRead(IAsyncResult ar) {
		if (ar.CompletedSynchronously) return;

		EndRead(ar);
		StartReceive();
	}

	private void EndRead(IAsyncResult ar) {
		int bytesRead;
		try {
			bytesRead = _sslStream.EndRead(ar);
		} catch (SocketException exc) {
			Log.Debug(exc, "SocketException '{e}' during EndRead.", exc.SocketErrorCode);
			NotifyReceiveCompleted(0);
			CloseInternal(exc.SocketErrorCode, "SocketException during EndRead.");
			return;
		} catch (ObjectDisposedException) {
			NotifyReceiveCompleted(0);
			CloseInternal(SocketError.SocketError, "SslStream disposed.");
			return;
		} catch (Exception exc) {
			Log.Debug(exc, "Exception during EndRead.");
			NotifyReceiveCompleted(0);
			CloseInternal(SocketError.SocketError, "Exception during EndRead.");
			return;
		}

		if (bytesRead <= 0) // socket closed normally
		{
			NotifyReceiveCompleted(0);
			CloseInternal(SocketError.Success, "Socket closed.");
			return;
		}

		NotifyReceiveCompleted(bytesRead);

		var buffer = TcpConnection.BufferManager.CheckOut();
		if (buffer.Array == null || buffer.Count == 0 || buffer.Array.Length < buffer.Offset + buffer.Count)
			throw new Exception("Invalid buffer allocated.");
		Buffer.BlockCopy(_receiveBuffer, 0, buffer.Array, buffer.Offset, bytesRead);
		var buf = new ArraySegment<byte>(buffer.Array, buffer.Offset, buffer.Count);
		_receiveQueue.Enqueue(new ReceivedData(buf, bytesRead));

		TryDequeueReceivedData();
	}

	private void TryDequeueReceivedData() {
		if (Interlocked.CompareExchange(ref _receiveHandling, 1, 0) != 0)
			return;
		do {
			if (!_receiveQueue.IsEmpty && _receiveCallback != null) {
				var callback = Interlocked.Exchange(ref _receiveCallback, null);
				if (callback == null) {
					Log.Fatal("Some threading issue in TryDequeueReceivedData! Callback is null!");
					throw new Exception("Some threading issue in TryDequeueReceivedData! Callback is null!");
				}

				var res = new List<ReceivedData>(_receiveQueue.Count);
				ReceivedData piece;
				while (_receiveQueue.TryDequeue(out piece)) {
					res.Add(piece);
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
					TcpConnection.BufferManager.CheckIn(res[i].Buf); // dispose buffers
				}

				NotifyReceiveDispatched(bytes);
			}

			Interlocked.Exchange(ref _receiveHandling, 0);
		} while (!_receiveQueue.IsEmpty
		         && _receiveCallback != null
		         && Interlocked.CompareExchange(ref _receiveHandling, 1, 0) == 0);
	}

	public void Close(string reason) {
		CloseInternal(SocketError.Success, reason ?? "Normal socket close."); // normal socket closing
	}

	private void CloseInternal(SocketError socketError, string reason) {
		lock (_closeLock) {
			if (_isClosing) return;
			_isClosing = true;
		}

		if (_sslStream != null)
			Helper.EatException(() => _sslStream.Close());

		if (_socket != null)
			Helper.EatException(() => _socket.Dispose());

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
				"DB {connectionType} closed [{dateTime:HH:mm:ss.fff}: N{remoteEndPoint}, L{localEndPoint}, {connectionId:B}]:Close reason: [{e}] {reason}",
				GetType().Name, DateTime.UtcNow, RemoteEndPoint, LocalEndPoint, _connectionId,
				socketError, reason);
		}

		var handler = ConnectionClosed;
		if (handler != null)
			handler(this, socketError);
	}

	public void SetClientConnectionName(string clientConnectionName) {
		_clientConnectionName = clientConnectionName;
	}

	public override string ToString() {
		return "S" + RemoteEndPoint;
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
