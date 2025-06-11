// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Plugins.Authentication;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Cluster;
using KurrentDB.Core.Cluster.Settings;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Services.Storage.EpochManager;
using KurrentDB.Core.Services.Transport.Tcp;
using KurrentDB.Core.TransactionLog.Chunks;
using KurrentDB.Core.TransactionLog.LogRecords;
using KurrentDB.Transport.Tcp;
using EndPoint = System.Net.EndPoint;
using ILogger = Serilog.ILogger;

namespace KurrentDB.Core.Services.Replication;

public class ReplicaService :
	IHandle<SystemMessage.StateChangeMessage>,
	IHandle<ReplicationMessage.ReconnectToLeader>,
	IAsyncHandle<ReplicationMessage.SubscribeToLeader>,
	IHandle<ReplicationMessage.AckLogPosition>,
	IHandle<ClientMessage.TcpForwardMessage> {

	private static readonly ILogger Log = Serilog.Log.ForContext<ReplicaService>();

	private readonly TcpClientConnector _connector = new();
	private readonly IPublisher _publisher;
	private readonly TFChunkDb _db;
	private readonly IEpochManager _epochManager;
	private readonly IPublisher _networkSendQueue;
	private readonly IAuthenticationProvider _authProvider;
	private readonly AuthorizationGateway _authorizationGateway;
	private readonly EndPoint _internalTcp;
	private readonly InternalTcpDispatcher _tcpDispatcher;
	private readonly bool _isReadOnlyReplica;
	private readonly bool _useSsl;
	private readonly CertificateDelegates.ServerCertificateValidator _sslServerCertValidator;
	private readonly Func<X509Certificate> _sslClientCertificateSelector;
	private readonly TimeSpan _heartbeatTimeout;
	private readonly TimeSpan _heartbeatInterval;
	private VNodeState _state = VNodeState.Initializing;
	private TcpConnectionManager _connection;

	public ReplicaService(
		IPublisher publisher,
		TFChunkDb db,
		IEpochManager epochManager,
		IPublisher networkSendQueue,
		IAuthenticationProvider authProvider,
		AuthorizationGateway authorizationGateway,
		EndPoint internalTcp,
		bool isReadOnlyReplica,
		bool useSsl,
		CertificateDelegates.ServerCertificateValidator sslServerCertValidator,
		Func<X509Certificate> sslClientCertificateSelector,
		TimeSpan heartbeatTimeout,
		TimeSpan heartbeatInterval,
		TimeSpan writeTimeout) {
		_isReadOnlyReplica = isReadOnlyReplica;
		_useSsl = useSsl;
		_sslServerCertValidator = sslServerCertValidator;
		_sslClientCertificateSelector = sslClientCertificateSelector;
		_heartbeatTimeout = heartbeatTimeout;
		_heartbeatInterval = heartbeatInterval;
		_publisher = Ensure.NotNull(publisher);
		_db = Ensure.NotNull(db);
		_epochManager = Ensure.NotNull(epochManager);
		_networkSendQueue = Ensure.NotNull(networkSendQueue);
		_authProvider = Ensure.NotNull(authProvider);
		_authorizationGateway = Ensure.NotNull(authorizationGateway);
		_internalTcp = Ensure.NotNull(internalTcp);
		_tcpDispatcher = new(writeTimeout);
	}

	public void Handle(SystemMessage.StateChangeMessage message) {
		if (message is SystemMessage.BecomeLeader)
			Log.Error("Replica");
		_state = message.State;

		switch (message.State) {
			case VNodeState.Initializing:
			case VNodeState.DiscoverLeader:
			case VNodeState.Unknown:
			case VNodeState.ReadOnlyLeaderless:
			case VNodeState.PreLeader:
			case VNodeState.Leader:
			case VNodeState.ResigningLeader:
			case VNodeState.ShuttingDown:
			case VNodeState.Shutdown: {
				Disconnect();
				break;
			}
			case VNodeState.PreReplica: {
				var m = (SystemMessage.BecomePreReplica)message;
				ConnectToLeader(m.LeaderConnectionCorrelationId, m.Leader);
				break;
			}
			case VNodeState.PreReadOnlyReplica: {
				var m = (SystemMessage.BecomePreReadOnlyReplica)message;
				ConnectToLeader(m.LeaderConnectionCorrelationId, m.Leader);
				break;
			}
			case VNodeState.CatchingUp:
			case VNodeState.Clone:
			case VNodeState.Follower:
			case VNodeState.ReadOnlyReplica: {
				// nothing changed, essentially
				break;
			}
			default:
				throw new ArgumentOutOfRangeException();
		}
	}

	private void Disconnect() {
		if (_connection != null) {
			_connection.Stop($"Node state changed to {_state}. Closing replication connection.");
			_connection = null;
		}
	}

	private void OnConnectionEstablished(TcpConnectionManager manager) {
		_publisher.Publish(new SystemMessage.VNodeConnectionEstablished(manager.RemoteEndPoint, manager.ConnectionId));
	}

	private void OnConnectionClosed(TcpConnectionManager manager, SocketError socketError) {
		_publisher.Publish(new SystemMessage.VNodeConnectionLost(manager.RemoteEndPoint, manager.ConnectionId));
	}

	public void Handle(ReplicationMessage.ReconnectToLeader message) {
		ConnectToLeader(message.ConnectionCorrelationId, message.Leader);
	}

	private void ConnectToLeader(Guid leaderConnectionCorrelationId, MemberInfo leader) {
		Debug.Assert(_state is VNodeState.PreReplica or VNodeState.PreReadOnlyReplica);

		var leaderEndPoint = GetLeaderEndPoint(leader, _useSsl);
		if (leaderEndPoint == null) {
			Log.Error("No valid endpoint found to connect to the Leader. Aborting connection operation to Leader.");
			return;
		}

		_connection?.Stop($"Reconnecting from old leader [{_connection.RemoteEndPoint}] to new leader: [{leaderEndPoint}].");

		try {
			_connection = new TcpConnectionManager(_useSsl ? "leader-secure" : "leader-normal",
				Guid.NewGuid(),
				_tcpDispatcher,
				_publisher,
				leaderEndPoint.GetHost(),
				leaderEndPoint.GetOtherNames(),
				leaderEndPoint,
				_connector,
				_useSsl,
				_sslServerCertValidator,
				() => {
					var cert = _sslClientCertificateSelector();
					return new X509CertificateCollection { cert };
				},
				_networkSendQueue,
				_authProvider,
				_authorizationGateway,
				_heartbeatInterval,
				_heartbeatTimeout,
				OnConnectionEstablished,
				OnConnectionClosed);
			_connection.StartReceiving();
		} catch (Exception ex) {
			Log.Error(ex, "Failed to connect to leader [{leader}]. This will be retried.", leader);
			_publisher.Publish(new ReplicationMessage.LeaderConnectionFailed(leaderConnectionCorrelationId, leader));
		}
	}

	private static EndPoint GetLeaderEndPoint(MemberInfo leader, bool useSsl) {
		Ensure.NotNull(leader);
		switch (useSsl) {
			case true when leader.InternalSecureTcpEndPoint == null:
				Log.Error(
					"Internal secure connections are required, but no internal secure TCP end point is specified for leader [{leader}]!",
					leader);
				break;
			case false when leader.InternalTcpEndPoint == null:
				Log.Error(
					"Internal connections are required, but no internal TCP end point is specified for leader [{leader}]!",
					leader);
				break;
		}

		return useSsl ? leader.InternalSecureTcpEndPoint : leader.InternalTcpEndPoint;
	}

	async ValueTask IAsyncHandle<ReplicationMessage.SubscribeToLeader>.HandleAsync(ReplicationMessage.SubscribeToLeader message, CancellationToken token) {
		if (_state is not VNodeState.PreReplica and not VNodeState.PreReadOnlyReplica)
			throw new Exception($"_state is {_state}, but is expected to be {VNodeState.PreReplica} or {VNodeState.PreReadOnlyReplica}");
		if (_connection is null) {
			Log.Warning("Attempted to subscribe to LEADER [{leaderId:B}], but no connection has been established. This will be retried.", message.LeaderId);
			return;
		}

		var logPosition = _db.Config.WriterCheckpoint.ReadNonFlushed();
		var epochs = await _epochManager.GetLastEpochs(ClusterConsts.SubscriptionLastEpochCount, token);

		Log.Information(
			"Subscribing at LogPosition: {logPosition} (0x{logPosition:X}) to LEADER [{remoteEndPoint}, {leaderId:B}] as replica with SubscriptionId: {subscriptionId:B}, "
			+ "ConnectionId: {connectionId:B}, LocalEndPoint: [{localEndPoint}], Epochs:\n{epochs}...\n.",
			logPosition, logPosition, _connection.RemoteEndPoint, message.LeaderId, message.SubscriptionId,
			_connection.ConnectionId, _connection.LocalEndPoint,
			string.Join("\n", epochs.Select(x => x.AsString())));

		var chunkId = Guid.Empty;

		// the chunk may not exist if it's a new database or if we're at a chunk boundary
		if (await _db.Manager.TryGetInitializedChunkFor(logPosition, token) is { } chunk)
			chunkId = chunk.ChunkHeader.ChunkId;

		SendTcpMessage(_connection,
			new ReplicationMessage.SubscribeReplica(
				version: ReplicationSubscriptionVersions.V_CURRENT,
				logPosition, chunkId, epochs, _internalTcp,
				message.LeaderId, message.SubscriptionId, isPromotable: !_isReadOnlyReplica));
	}

	public void Handle(ReplicationMessage.AckLogPosition message) {
		if (!_state.IsReplica())
			throw new Exception("!_state.IsReplica()");
		if (_connection == null)
			throw new Exception("_connection == null");
		SendTcpMessage(_connection, message);
	}

	public void Handle(ClientMessage.TcpForwardMessage message) {
		switch (_state) {
			case VNodeState.PreReplica:
			case VNodeState.PreReadOnlyReplica: {
				if (_connection != null)
					SendTcpMessage(_connection, message.Message);
				break;
			}
			case VNodeState.CatchingUp:
			case VNodeState.Clone:
			case VNodeState.Follower:
			case VNodeState.ReadOnlyReplica: {
				Debug.Assert(_connection != null, "Connection manager is null in follower/clone/catching up state");
				SendTcpMessage(_connection, message.Message);
				break;
			}

			default:
				throw new Exception($"Unexpected state: {_state}");
		}
	}

	private void SendTcpMessage(TcpConnectionManager manager, Message msg) {
		_networkSendQueue.Publish(new TcpMessage.TcpSend(manager, msg));
	}
}
