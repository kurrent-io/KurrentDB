// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DotNext.Runtime.CompilerServices;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Services.Monitoring.Stats;
using KurrentDB.Core.Services.UserManagement;
using KurrentDB.Transport.Tcp;
using Serilog.Core;
using ILogger = Serilog.ILogger;

namespace KurrentDB.Core.Services.Monitoring;

[Flags]
public enum StatsStorage {
	None = 0x0, // only for tests
	Stream = 0x1,
	File = 0x2,
	StreamAndFile = Stream | File
}

public class MonitoringService : IHandle<SystemMessage.SystemInit>,
	IHandle<SystemMessage.StateChangeMessage>,
	IAsyncHandle<SystemMessage.BecomeShuttingDown>,
	IHandle<SystemMessage.BecomeShutdown>,
	IHandle<ClientMessage.WriteEventsCompleted>,
	IAsyncHandle<MonitoringMessage.GetFreshStats>,
	IHandle<MonitoringMessage.GetFreshTcpConnectionStats> {
	private static readonly ILogger RegularLog = Serilog.Log.ForContext(Constants.SourceContextPropertyName, "REGULAR-STATS-LOGGER");
	private static readonly ILogger Log = Serilog.Log.ForContext<MonitoringService>();

	private static readonly string StreamMetadata = $"{{\"$maxAge\":{(int)TimeSpan.FromDays(10).TotalSeconds}}}";

	public static readonly TimeSpan MemoizePeriod = TimeSpan.FromSeconds(1);
	private static readonly IEnvelope NoopEnvelope = new NoopEnvelope();

	private readonly IQueuedHandler _monitoringQueue;
	private readonly IAsyncHandle<Message> _statsCollectionDispatcher;
	private readonly IPublisher _mainQueue;
	private readonly StatsStorage _statsStorage;
	private readonly TimeSpan _statsCollectionPeriod;
	private readonly SystemStatsHelper _systemStats;

	private DateTime _lastStatsRequestTime = DateTime.UtcNow;
	private StatsContainer _memoizedStats;
	private Task _timer;
	private CancellationTokenSource _timerTokenSource;
	private readonly CancellationToken _timerToken;

	private readonly string _nodeStatsStream;
	private bool _statsStreamCreated;
	private Guid _streamMetadataWriteCorrId;
	private IMonitoredTcpConnection[] _memoizedTcpConnections;
	private DateTime _lastTcpConnectionsRequestTime;
	private readonly IPEndPoint _tcpEndpoint;
	private readonly IPEndPoint _tcpSecureEndpoint;

	public MonitoringService(IQueuedHandler monitoringQueue,
		IAsyncHandle<Message> statsCollectionDispatcher,
		IPublisher mainQueue,
		TimeSpan statsCollectionPeriod,
		EndPoint nodeEndpoint,
		StatsStorage statsStorage,
		IPEndPoint tcpEndpoint,
		IPEndPoint tcpSecureEndpoint,
		SystemStatsHelper systemStatsHelper) {
		Ensure.NotNull(nodeEndpoint);

		_monitoringQueue = Ensure.NotNull(monitoringQueue);
		_statsCollectionDispatcher = Ensure.NotNull(statsCollectionDispatcher);
		_mainQueue = Ensure.NotNull(mainQueue);
		_statsStorage = statsStorage;

		_statsCollectionPeriod = statsCollectionPeriod;

		if (statsCollectionPeriod > TimeSpan.Zero) {
			_timerTokenSource = new();
			_timerToken = _timerTokenSource.Token;
		} else {
			_timerToken = new(canceled: true);
		}

		_nodeStatsStream = $"{SystemStreams.StatsStreamPrefix}-{nodeEndpoint}";
		_tcpEndpoint = tcpEndpoint;
		_tcpSecureEndpoint = tcpSecureEndpoint;

		_timer = Task.CompletedTask;
		_systemStats = systemStatsHelper;
	}

	public void Handle(SystemMessage.SystemInit message) {
		if (_timerToken.IsCancellationRequested)
			return;

		_timer = CollectRegularStatsJob();
	}

	[AsyncMethodBuilder(typeof(SpawningAsyncTaskMethodBuilder))]
	private async Task CollectRegularStatsJob() {
		_systemStats.Start();

		while (true) {
			await Task.Delay(_statsCollectionPeriod, _timerToken)
				.ConfigureAwait(
					ConfigureAwaitOptions.SuppressThrowing |
					// to be consistent with all the other awaits
					ConfigureAwaitOptions.ContinueOnCapturedContext);

			if (_timerToken.IsCancellationRequested)
				break;

			await CollectRegularStats(_timerToken);
		}
	}

	private async ValueTask CollectRegularStats(CancellationToken token) {
		try {
			if (await CollectStats(token) is { } stats) {
				var rawStats = stats.GetStats(useGrouping: false, useMetadata: false);

				if ((_statsStorage & StatsStorage.File) != 0)
					SaveStatsToFile(StatsContainer.Group(rawStats));

				if ((_statsStorage & StatsStorage.Stream) != 0) {
					if (_statsStreamCreated)
						SaveStatsToStream(rawStats);
				}
			}
		} catch (Exception ex) {
			Log.Error(ex, "Error on regular stats collection.");
		}
	}

	private async ValueTask<StatsContainer> CollectStats(CancellationToken token) {
		var statsContainer = new StatsContainer();
		try {
			statsContainer.Add(_systemStats.GetSystemStats());
			await _statsCollectionDispatcher.HandleAsync(new MonitoringMessage.InternalStatsRequest(new StatsCollectorEnvelope(statsContainer)), token);
		} catch (OperationCanceledException e) when (e.CancellationToken == token) {
			statsContainer = null;
		} catch (Exception ex) {
			Log.Error(ex, "Error while collecting stats");
			statsContainer = null;
		}

		return statsContainer;
	}

	private static void SaveStatsToFile(Dictionary<string, object> rawStats) {
		rawStats.Add("timestamp", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
		RegularLog.Information("{@stats}", rawStats);
	}

	private void SaveStatsToStream(Dictionary<string, object> rawStats) {
		var data = rawStats.ToJsonBytes();
		var evnt = new Event(Guid.NewGuid(), SystemEventTypes.StatsCollection, true, data, null, SchemaInfo.None, SchemaInfo.None);
		var corrId = Guid.NewGuid();
		var msg = new ClientMessage.WriteEvents(corrId, corrId, NoopEnvelope, false, _nodeStatsStream, ExpectedVersion.Any, [evnt], SystemAccounts.System);
		_mainQueue.Publish(msg);
	}

	public void Handle(SystemMessage.StateChangeMessage message) {
		if ((_statsStorage & StatsStorage.Stream) == 0)
			return;

		if (_statsStreamCreated)
			return;

		switch (message.State) {
			case VNodeState.CatchingUp:
			case VNodeState.Clone:
			case VNodeState.Follower:
			case VNodeState.ReadOnlyReplica:
			case VNodeState.Leader: {
				SetStatsStreamMetadata();
				break;
			}
		}
	}

	public async ValueTask HandleAsync(SystemMessage.BecomeShuttingDown message, CancellationToken token) {
		if (Interlocked.Exchange(ref _timerTokenSource, null) is { } cts) {
			try {
				cts.Cancel();
				await _timer.WaitAsync(token);
			} finally {
				cts.Dispose();
				_systemStats.Dispose();
			}
		}
	}

	public void Handle(SystemMessage.BecomeShutdown message) {
		_monitoringQueue.RequestStop();
	}

	private void SetStatsStreamMetadata() {
		var metadata = Helper.UTF8NoBom.GetBytes(StreamMetadata);
		_streamMetadataWriteCorrId = Guid.NewGuid();
		_mainQueue.Publish(
			new ClientMessage.WriteEvents(
				_streamMetadataWriteCorrId, _streamMetadataWriteCorrId, _monitoringQueue,
				false, SystemStreams.MetastreamOf(_nodeStatsStream), ExpectedVersion.NoStream,
				[new Event(Guid.NewGuid(), SystemEventTypes.StreamMetadata, true, metadata, null, SchemaInfo.None, SchemaInfo.None)],
				SystemAccounts.System));
	}

	public void Handle(ClientMessage.WriteEventsCompleted message) {
		if (message.CorrelationId != _streamMetadataWriteCorrId)
			return;
		switch (message.Result) {
			case OperationResult.Success:
			case OperationResult.WrongExpectedVersion: // already created
			{
				Log.Debug("Created stats stream '{stream}', code = {result}", _nodeStatsStream, message.Result);
				_statsStreamCreated = true;
				break;
			}
			case OperationResult.PrepareTimeout:
			case OperationResult.CommitTimeout:
			case OperationResult.ForwardTimeout: {
				Log.Debug("Failed to create stats stream '{stream}'. Reason : {e}({message}). Retrying...", _nodeStatsStream, message.Result, message.Message);
				SetStatsStreamMetadata();
				break;
			}
			case OperationResult.AccessDenied: {
				// can't do anything about that right now
				break;
			}
			case OperationResult.StreamDeleted:
			case OperationResult.InvalidTransaction: // should not happen at all
			{
				Log.Error(
					"Monitoring service got unexpected response code when trying to create stats stream ({e}).",
					message.Result);
				break;
			}
			default:
				throw new ArgumentOutOfRangeException();
		}
	}

	public async ValueTask HandleAsync(MonitoringMessage.GetFreshStats message, CancellationToken token) {
		try {
			if (!TryGetMemoizedStats(out var stats)) {
				stats = await CollectStats(token);
				if (stats != null) {
					_memoizedStats = stats;
					_lastStatsRequestTime = DateTime.UtcNow;
				}
			}

			Dictionary<string, object> selectedStats = null;
			if (stats != null) {
				selectedStats = stats.GetStats(message.UseGrouping, message.UseMetadata);
				if (message.UseGrouping)
					selectedStats = message.StatsSelector(selectedStats);
			}

			message.Envelope.ReplyWith(new MonitoringMessage.GetFreshStatsCompleted(success: selectedStats != null, stats: selectedStats));
		} catch (Exception ex) {
			Log.Error(ex, "Error on getting fresh stats");
		}
	}

	public void Handle(MonitoringMessage.GetFreshTcpConnectionStats message) {
		try {
			if (!TryGetMemoizedTcpConnections(out var connections)) {
				connections = TcpConnectionMonitor.Default.GetTcpConnectionStats();
				if (connections != null) {
					_memoizedTcpConnections = connections;
					_lastTcpConnectionsRequestTime = DateTime.UtcNow;
				}
			}

			List<MonitoringMessage.TcpConnectionStats> connStats = [];
			foreach (var conn in connections!) {
				switch (conn) {
					case TcpConnection tcpConn: {
						var isExternalConnection = _tcpEndpoint != null && _tcpEndpoint.Port == tcpConn.LocalEndPoint.GetPort();
						connStats.Add(new() {
							IsExternalConnection = isExternalConnection,
							RemoteEndPoint = tcpConn.RemoteEndPoint.ToString(),
							LocalEndPoint = tcpConn.LocalEndPoint.ToString(),
							ConnectionId = tcpConn.ConnectionId,
							ClientConnectionName = tcpConn.ClientConnectionName,
							TotalBytesSent = tcpConn.TotalBytesSent,
							TotalBytesReceived = tcpConn.TotalBytesReceived,
							PendingSendBytes = tcpConn.PendingSendBytes,
							PendingReceivedBytes = tcpConn.PendingReceivedBytes,
							IsSslConnection = false
						});
						break;
					}
					case TcpConnectionSsl tcpConnSsl: {
						var isExternalConnection = _tcpSecureEndpoint != null && _tcpSecureEndpoint.Port == tcpConnSsl.LocalEndPoint.GetPort();
						connStats.Add(new() {
							IsExternalConnection = isExternalConnection,
							RemoteEndPoint = tcpConnSsl.RemoteEndPoint.ToString(),
							LocalEndPoint = tcpConnSsl.LocalEndPoint.ToString(),
							ConnectionId = tcpConnSsl.ConnectionId,
							ClientConnectionName = tcpConnSsl.ClientConnectionName,
							TotalBytesSent = tcpConnSsl.TotalBytesSent,
							TotalBytesReceived = tcpConnSsl.TotalBytesReceived,
							PendingSendBytes = tcpConnSsl.PendingSendBytes,
							PendingReceivedBytes = tcpConnSsl.PendingReceivedBytes,
							IsSslConnection = true
						});
						break;
					}
				}
			}

			message.Envelope.ReplyWith(new MonitoringMessage.GetFreshTcpConnectionStatsCompleted(connStats));
		} catch (Exception ex) {
			Log.Error(ex, "Error on getting fresh tcp connection stats");
		}
	}

	private bool TryGetMemoizedStats(out StatsContainer stats) {
		if (_memoizedStats == null || DateTime.UtcNow - _lastStatsRequestTime > MemoizePeriod) {
			stats = null;
			return false;
		}

		stats = _memoizedStats;
		return true;
	}

	private bool TryGetMemoizedTcpConnections([NotNullWhen(false)] out IMonitoredTcpConnection[] connections) {
		if (_memoizedTcpConnections == null || DateTime.UtcNow - _lastTcpConnectionsRequestTime > MemoizePeriod) {
			connections = null;
			return false;
		}

		connections = _memoizedTcpConnections;
		return true;
	}
}
