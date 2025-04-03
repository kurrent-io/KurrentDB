// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using EventStore.Common.Utils;
using EventStore.Core.Bus;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Services.TimerService;
using EventStore.Transport.Tcp;
using EventStore.Transport.Tcp.Framing;
using EventStore.Core.Settings;
using EventStore.Plugins.Authentication;
using ILogger = Serilog.ILogger;

namespace EventStore.Core.Services.Transport.Tcp;

/// <summary>
/// Manager for individual TCP connection. It handles connection lifecycle,
/// heartbeats, message framing and dispatch to the memory bus.
/// </summary>
public class TcpConnectionManager : IHandle<TcpMessage.Heartbeat>, IHandle<TcpMessage.HeartbeatTimeout> {
	public static readonly TimeSpan ConnectionTimeout = TimeSpan.FromMilliseconds(1000);
	private static readonly ILogger Log = Serilog.Log.ForContext<TcpConnectionManager>();
	private static readonly ClaimsPrincipal Anonymous = new(new ClaimsIdentity([new(ClaimTypes.Anonymous, "")]));
	public readonly Guid ConnectionId;
	public readonly string ConnectionName;
	public readonly EndPoint RemoteEndPoint;

	public EndPoint LocalEndPoint => _connection.LocalEndPoint;

	public bool IsClosed => _isClosed != 0;

	public int SendQueueSize => _connection.SendQueueSize;

	public string ClientConnectionName => _clientConnectionName;

	private readonly ITcpConnection _connection;
	private readonly SendOverTcpEnvelope _tcpEnvelope;
	private readonly IPublisher _publisher;
	private readonly ITcpDispatcher _dispatcher;
	private readonly IMessageFramer<ArraySegment<byte>> _framer;
	private long ReceiveProgressIndicator => _connection?.TotalBytesReceived ?? 0L;
	private long SendProgressIndicator => _connection?.TotalBytesSent ?? 0L;
	private int _isClosed;
	private string _clientConnectionName;

	private byte _version;

	private readonly Action<TcpConnectionManager, SocketError> _connectionClosed;
	private readonly Action<TcpConnectionManager> _connectionEstablished;

	private readonly SendToWeakThisEnvelope _weakThisEnvelope;
	private readonly TimeSpan _heartbeatInterval;
	private readonly TimeSpan _heartbeatTimeout;
	private bool _awaitingHeartbeatTimeoutCheck;
	private readonly int _connectionPendingSendBytesThreshold;
	private readonly int _connectionQueueSizeThreshold;

	private readonly IAuthenticationProvider _authProvider;
	private readonly AuthorizationGateway _authorization;
	private UserCredentials _defaultUser;
	private readonly TcpServiceType _serviceType;

	public TcpConnectionManager(string connectionName,
		TcpServiceType serviceType,
		ITcpDispatcher dispatcher,
		IPublisher publisher,
		ITcpConnection openedConnection,
		IPublisher networkSendQueue,
		IAuthenticationProvider authProvider,
		AuthorizationGateway authorization,
		TimeSpan heartbeatInterval,
		TimeSpan heartbeatTimeout,
		Action<TcpConnectionManager, SocketError> onConnectionClosed,
		int connectionPendingSendBytesThreshold,
		int connectionQueueSizeThreshold) {
		_connection = Ensure.NotNull(openedConnection);
		ConnectionId = openedConnection.ConnectionId;
		ConnectionName = connectionName;
		_serviceType = serviceType;
		_tcpEnvelope = new(this, Ensure.NotNull(networkSendQueue));
		_publisher = Ensure.NotNull(publisher);
		_dispatcher = Ensure.NotNull(dispatcher);
		_authProvider = Ensure.NotNull(authProvider);
		_authorization = Ensure.NotNull(authorization);

		_framer = new LengthPrefixMessageFramer();
		_framer.RegisterMessageArrivedCallback(OnMessageArrived);

		_weakThisEnvelope = new(this);
		_heartbeatInterval = heartbeatInterval;
		_heartbeatTimeout = heartbeatTimeout;
		_connectionPendingSendBytesThreshold = connectionPendingSendBytesThreshold;
		_connectionQueueSizeThreshold = connectionQueueSizeThreshold;
		_connectionClosed = onConnectionClosed;
		RemoteEndPoint = openedConnection.RemoteEndPoint;
		_connection.ConnectionClosed += OnConnectionClosed;
		if (_connection.IsClosed) {
			OnConnectionClosed(_connection, SocketError.Success);
			return;
		}

		ScheduleHeartbeat(0L, 0L);
	}

	public TcpConnectionManager(string connectionName,
		Guid connectionId,
		ITcpDispatcher dispatcher,
		IPublisher publisher,
		string targetHost,
		string[] otherNames,
		EndPoint remoteEndPoint,
		TcpClientConnector connector,
		bool useSsl,
		CertificateDelegates.ServerCertificateValidator sslServerCertValidator,
		Func<X509CertificateCollection> sslClientCertificatesSelector,
		IPublisher networkSendQueue,
		IAuthenticationProvider authProvider,
		AuthorizationGateway authorization,
		TimeSpan heartbeatInterval,
		TimeSpan heartbeatTimeout,
		Action<TcpConnectionManager> onConnectionEstablished,
		Action<TcpConnectionManager, SocketError> onConnectionClosed) {
		Ensure.NotNull(connector);

		ConnectionId = Ensure.NotEmptyGuid(connectionId);
		ConnectionName = connectionName;

		_tcpEnvelope = new(this, networkSendQueue);
		_publisher = Ensure.NotNull(publisher);
		_dispatcher = Ensure.NotNull(dispatcher);
		_authProvider = Ensure.NotNull(authProvider);
		_authorization = Ensure.NotNull(authorization);

		_framer = new LengthPrefixMessageFramer();
		_framer.RegisterMessageArrivedCallback(OnMessageArrived);

		_weakThisEnvelope = new(this);
		_heartbeatInterval = heartbeatInterval;
		_heartbeatTimeout = heartbeatTimeout;
		_connectionPendingSendBytesThreshold = ESConsts.UnrestrictedPendingSendBytes;
		_connectionQueueSizeThreshold = ESConsts.MaxConnectionQueueSize;

		_connectionEstablished = onConnectionEstablished;
		_connectionClosed = onConnectionClosed;

		RemoteEndPoint = Ensure.NotNull(remoteEndPoint);
		_connection = useSsl
			? connector.ConnectSslTo(ConnectionId, targetHost, otherNames, remoteEndPoint.ResolveDnsToIPAddress(), ConnectionTimeout,
				sslServerCertValidator, sslClientCertificatesSelector, OnConnectionEstablished, OnConnectionFailed)
			: connector.ConnectTo(ConnectionId, remoteEndPoint.ResolveDnsToIPAddress(), ConnectionTimeout, OnConnectionEstablished,
				OnConnectionFailed);
		_connection.ConnectionClosed += OnConnectionClosed;
		if (_connection.IsClosed)
			OnConnectionClosed(_connection, SocketError.Success);
	}

	private void OnConnectionEstablished(ITcpConnection connection) {
		Log.Information("Connection '{connectionName}' ({connectionId:B}) to [{remoteEndPoint}] established.", ConnectionName, ConnectionId, connection.RemoteEndPoint);

		ScheduleHeartbeat(0L, 0L);

		_connectionEstablished?.Invoke(this);
	}

	private void OnConnectionFailed(ITcpConnection connection, SocketError socketError) {
		if (Interlocked.CompareExchange(ref _isClosed, 1, 0) != 0) return;
		Log.Information("Connection '{connectionName}' ({connectionId:B}) to [{remoteEndPoint}] failed: {e}.",
			ConnectionName, ConnectionId, connection.RemoteEndPoint, socketError);
		_connectionClosed?.Invoke(this, socketError);
	}

	private void OnConnectionClosed(ITcpConnection connection, SocketError socketError) {
		if (Interlocked.CompareExchange(ref _isClosed, 1, 0) != 0) return;
		Log.Information(
			"Connection '{connectionName}{clientConnectionName}' [{remoteEndPoint}, {connectionId:B}] closed: {e}.",
			ConnectionName, ClientConnectionName.IsEmptyString() ? string.Empty : ":" + ClientConnectionName,
			connection.RemoteEndPoint, ConnectionId, socketError);

		if (_serviceType == TcpServiceType.Internal && _connection.TotalBytesSent == 0) {
			Log.Warning(
				"Unable to connect to Remote EndPoint : {remoteEndPoint}. Connection is potentially blocked. Total bytes sent: {TotalBytesSent}, Total bytes received: {TotalBytesReceived}",
				RemoteEndPoint, _connection.TotalBytesSent, _connection.TotalBytesReceived);
		}

		_connectionClosed?.Invoke(this, socketError);
	}

	public void StartReceiving() {
		_connection.ReceiveAsync(OnRawDataReceived);
	}

	private void OnRawDataReceived(ITcpConnection connection, IEnumerable<ArraySegment<byte>> data) {
		try {
			_framer.UnFrameData(data);
		} catch (PackageFramingException exc) {
			SendBadRequestAndClose(Guid.Empty, $"Invalid TCP frame received. Error: {exc.Message}.");
			return;
		}

		_connection.ReceiveAsync(OnRawDataReceived);
	}

	private void OnMessageArrived(ArraySegment<byte> data) {
		TcpPackage package;
		try {
			package = TcpPackage.FromArraySegment(data);
		} catch (Exception e) {
			SendBadRequestAndClose(Guid.Empty, $"Received bad network package. Error: {e}");
			return;
		}

		OnPackageReceived(package);
	}

	private void OnPackageReceived(TcpPackage package) {
		try {
			ProcessPackage(package);
		} catch (Exception e) {
			SendBadRequestAndClose(package.CorrelationId, $"Error while processing package. Error: {e}");
		}
	}

	public void ProcessPackage(TcpPackage package) {
		if (_serviceType == TcpServiceType.External && (package.Flags & TcpFlags.TrustedWrite) != 0) {
			SendBadRequestAndClose(package.CorrelationId, "Trusted writes aren't accepted over the external TCP interface");
			return;
		}

		switch (package.Command) {
			case TcpCommand.HeartbeatResponseCommand:
				break;
			case TcpCommand.HeartbeatRequestCommand:
				SendPackage(new TcpPackage(TcpCommand.HeartbeatResponseCommand, package.CorrelationId, null));
				break;
			case TcpCommand.IdentifyClient: {
				try {
					var message = (ClientMessage.IdentifyClient)_dispatcher.UnwrapPackage(package, _tcpEnvelope, null, null, this, _version);
					Log.Information(
						"Connection '{connectionName}' ({connectionId:B}) identified by client. Client connection name: '{clientConnectionName}', Client version: {clientVersion}.",
						ConnectionName, ConnectionId, message.ConnectionName, (ClientVersion)message.Version);
					_version = (byte)message.Version;
					_clientConnectionName = message.ConnectionName;
					_connection.SetClientConnectionName(_clientConnectionName);
					SendPackage(new TcpPackage(TcpCommand.ClientIdentified, package.CorrelationId, null));
				} catch (Exception ex) {
					Log.Error("Error identifying client: {e}", ex);
				}

				break;
			}
			case TcpCommand.BadRequest: {
				var reason = string.Empty;
				Helper.EatException(() => reason = Helper.UTF8NoBom.GetString(package.Data.Array, package.Data.Offset, package.Data.Count));
				Log.Error(
					"Bad request received from '{connectionName}{clientConnectionName}' [{remoteEndPoint}, L{localEndPoint}, {connectionId:B}]. CorrelationId: {correlationId:B}, Error: {e}.",
					ConnectionName,
					ClientConnectionName.IsEmptyString() ? string.Empty : ":" + ClientConnectionName,
					RemoteEndPoint,
					LocalEndPoint, ConnectionId, package.CorrelationId,
					reason.IsEmptyString() ? "<reason missing>" : reason);
				break;
			}
			case TcpCommand.Authenticate: {
				if ((package.Flags & TcpFlags.Authenticated) == 0)
					ReplyNotAuthenticated(package.CorrelationId, "No user credentials provided.");
				else {
					var defaultUser = new UserCredentials(package.Tokens, null);
					Interlocked.Exchange(ref _defaultUser, defaultUser);
					_authProvider.Authenticate(new TcpDefaultAuthRequest(this, package.CorrelationId, defaultUser));
				}

				break;
			}
			default: {
				var defaultUser = _defaultUser;
				if ((package.Flags & TcpFlags.TrustedWrite) != 0) {
					UnwrapAndPublishPackage(package, UserManagement.SystemAccounts.System, null);
				} else if ((package.Flags & TcpFlags.Authenticated) != 0) {
					_authProvider.Authenticate(new TcpAuthRequest(this, package));
				} else if (defaultUser != null) {
					if (defaultUser.User != null) {
						UnwrapAndPublishPackage(package, defaultUser.User, defaultUser.Tokens);
					} else {
						// The default credentials aren't authenticated
						ReplyNotAuthenticated(package.CorrelationId, "Not Authenticated");
					}
				} else {
					UnwrapAndPublishPackage(package, Anonymous, null);
				}

				break;
			}
		}
	}

	private void UnwrapAndPublishPackage(TcpPackage package, ClaimsPrincipal user, IReadOnlyDictionary<string, string> tokens) {
		Message message = null;
		string error = "";
		try {
			message = _dispatcher.UnwrapPackage(package, _tcpEnvelope, user, tokens, this, _version);
		} catch (Exception ex) {
			error = ex.Message;
		}

		if (message != null)
			_authorization.Authorize(message, _publisher);
		else
			SendBadRequest(package.CorrelationId, $"Could not unwrap network package for command {package.Command}.\n{error}");
	}

	private void ReplyNotAuthenticated(Guid correlationId, string description) {
		_tcpEnvelope.ReplyWith(new TcpMessage.NotAuthenticated(correlationId, description));
	}

	private void ReplyNotReady(Guid correlationId, string description) {
		_tcpEnvelope.ReplyWith(new ClientMessage.NotHandled(correlationId, ClientMessage.NotHandled.Types.NotHandledReason.NotReady, description));
	}

	private void ReplyAuthenticated(Guid correlationId, UserCredentials userCredentials, ClaimsPrincipal user) {
		var authCredentials = new UserCredentials(userCredentials.Tokens, user);
		Interlocked.CompareExchange(ref _defaultUser, authCredentials, userCredentials);
		_tcpEnvelope.ReplyWith(new TcpMessage.Authenticated(correlationId));
	}

	public void SendBadRequestAndClose(Guid correlationId, string message) {
		Ensure.NotNull(message);

		SendPackage(new(TcpCommand.BadRequest, correlationId, Helper.UTF8NoBom.GetBytes(message)), checkQueueSize: false);
		Log.Error(
			"Closing connection '{connectionName}{clientConnectionName}' [{remoteEndPoint}, L{localEndPoint}, {connectionId:B}] due to error. Reason: {e}",
			ConnectionName, ClientConnectionName.IsEmptyString() ? string.Empty : ":" + ClientConnectionName,
			RemoteEndPoint, LocalEndPoint, ConnectionId, message);
		_connection.Close(message);
	}

	public void SendBadRequest(Guid correlationId, string message) {
		Ensure.NotNull(message);

		SendPackage(new(TcpCommand.BadRequest, correlationId, Helper.UTF8NoBom.GetBytes(message)), checkQueueSize: false);
	}

	public void Stop(string reason = null) {
		Log.Debug(
			"Closing connection '{connectionName}{clientConnectionName}' [{remoteEndPoint}, L{localEndPoint}, {connectionId:B}] cleanly.{reason}",
			ConnectionName, ClientConnectionName.IsEmptyString() ? string.Empty : ":" + ClientConnectionName,
			RemoteEndPoint, LocalEndPoint, ConnectionId,
			reason.IsEmpty() ? string.Empty : $"Reason: {reason}");
		_connection.Close(reason);
	}

	public void SendMessage(Message message) {
		var package = _dispatcher.WrapMessage(message, _version);
		if (package != null)
			SendPackage(package.Value);
	}

	private void SendPackage(TcpPackage package, bool checkQueueSize = true) {
		if (IsClosed)
			return;

		if (checkQueueSize) {
			int queueSize;
			if ((queueSize = _connection.SendQueueSize) > _connectionQueueSizeThreshold) {
				SendBadRequestAndClose(Guid.Empty, $"Connection queue size is too large: {queueSize}.");
				return;
			}

			int queueSendBytes;
			if (_connectionPendingSendBytesThreshold > ESConsts.UnrestrictedPendingSendBytes &&
			    (queueSendBytes = _connection.PendingSendBytes) > _connectionPendingSendBytesThreshold) {
				SendBadRequestAndClose(Guid.Empty, $"Connection pending send bytes is too large: {queueSendBytes}.");
				return;
			}
		}

		var data = package.AsArraySegment();
		var framed = _framer.FrameData(data);
		_connection.EnqueueSend(framed);
	}

	public void Handle(TcpMessage.Heartbeat message) {
		if (IsClosed) return;

		var receiveProgressIndicator = ReceiveProgressIndicator;
		var sendProgressIndicator = SendProgressIndicator;

		// If we have not received any data within the heartbeat interval, and we are not waiting for a heartbeat timeout check,
		// then we send a heartbeat request.
		if (message.ReceiveProgressIndicator == receiveProgressIndicator && !_awaitingHeartbeatTimeoutCheck) {
			SendPackage(new(TcpCommand.HeartbeatRequestCommand, Guid.NewGuid(), null));
			_awaitingHeartbeatTimeoutCheck = true;
			_publisher.Publish(TimerMessage.Schedule.Create(_heartbeatTimeout, _weakThisEnvelope,
				new TcpMessage.HeartbeatTimeout(receiveProgressIndicator)));
		}
		// As a proactive measure, if we have not sent any data to the remote party within the heartbeat interval,
		// we also send a heartbeat request just to generate some data for the remote party so that it can clear its heartbeat timeouts.
		// This is particularly useful when the remote party's heartbeat request is stuck behind a large (multi-megabyte) TCP message.
		else if (message.SendProgressIndicator == sendProgressIndicator) {
			SendPackage(new(TcpCommand.HeartbeatRequestCommand, Guid.NewGuid(), null));
			Log.Verbose(
				"Connection '{connectionName}{clientConnectionName}' [{remoteEndPoint}, {connectionId:B}] Proactive heartbeat request sent to the remote party since no data was sent during the last heartbeat interval.",
				ConnectionName, ClientConnectionName.IsEmptyString() ? string.Empty : $":{ClientConnectionName}",
				_connection.RemoteEndPoint, ConnectionId);
		}

		// Schedule the next heartbeat regardless of whether or not there's an active heartbeat request
		ScheduleHeartbeat(receiveProgressIndicator, sendProgressIndicator);
	}

	public void Handle(TcpMessage.HeartbeatTimeout message) {
		_awaitingHeartbeatTimeoutCheck = false;
		if (IsClosed) return;

		var receiveProgressIndicator = ReceiveProgressIndicator;
		var sendProgressIndicator = SendProgressIndicator;
		if (message.ReceiveProgressIndicator == receiveProgressIndicator)
			Stop(string.Format($"HEARTBEAT TIMEOUT at receiveProgressIndicator={receiveProgressIndicator}, sendProgressIndicator={sendProgressIndicator}"));
	}

	private void ScheduleHeartbeat(long receiveProgressIndicator, long sendProgressIndicator) {
		_publisher.Publish(TimerMessage.Schedule.Create(_heartbeatInterval, _weakThisEnvelope,
			new TcpMessage.Heartbeat(receiveProgressIndicator, sendProgressIndicator)));
	}

	// Same health warnings as SendToThisEnvelope
	private class SendToWeakThisEnvelope(object receiver) : IEnvelope {
		private readonly WeakReference _receiver = new(receiver);

		public void ReplyWith<T>(T message) where T : Message {
			if (_receiver.Target is IHandle<T> handle) {
				handle.Handle(message);
			} else if (_receiver is IAsyncHandle<T>) {
				throw new Exception($"SendToWeakThisEnvelope does not support asynchronous receivers. Receiver: {_receiver}");
			}
		}
	}

	private class TcpAuthRequest(TcpConnectionManager manager, TcpPackage package)
		: AuthenticationRequest($"(TCP) {manager.RemoteEndPoint}", package.Tokens) {
		private readonly TcpPackage _package = package;

		public override void Unauthorized() {
			manager.ReplyNotAuthenticated(_package.CorrelationId, "Not Authenticated");
		}

		public override void Authenticated(ClaimsPrincipal principal) {
			manager.UnwrapAndPublishPackage(_package, principal, _package.Tokens);
		}

		public override void Error() {
			manager.ReplyNotAuthenticated(_package.CorrelationId, "Internal Server Error");
		}

		public override void NotReady() {
			manager.ReplyNotReady(_package.CorrelationId, "Server not ready");
		}
	}

	private class TcpDefaultAuthRequest(TcpConnectionManager manager, Guid correlationId, UserCredentials userCredentials)
		: AuthenticationRequest($"(TCP) {manager.RemoteEndPoint}", userCredentials.Tokens) {
		public override void Unauthorized() {
			manager.ReplyNotAuthenticated(correlationId, "Unauthorized");
		}

		public override void Authenticated(ClaimsPrincipal principal) {
			manager.ReplyAuthenticated(correlationId, userCredentials, principal);
		}

		public override void Error() {
			manager.ReplyNotAuthenticated(correlationId, "Internal Server Error");
		}

		public override void NotReady() {
			manager.ReplyNotReady(correlationId, "Server not yet ready");
		}
	}

	record UserCredentials(IReadOnlyDictionary<string, string> Tokens, ClaimsPrincipal User);
}
