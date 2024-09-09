using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DotNext.Runtime.CompilerServices;
using DotNext.Threading;
using EventStore.Common.Utils;
using EventStore.Core.Bus;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Services.Monitoring.Stats;
using EventStore.Core.Services.UserManagement;
using EventStore.Core.TransactionLog.Checkpoint;
using EventStore.Transport.Tcp;
using ILogger = Serilog.ILogger;
using Timeout = System.Threading.Timeout;

namespace EventStore.Core.Services.Monitoring {
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
		private static readonly ILogger RegularLog =
			Serilog.Log.ForContext(Serilog.Core.Constants.SourceContextPropertyName, "REGULAR-STATS-LOGGER");
		private static readonly ILogger Log = Serilog.Log.ForContext<MonitoringService>();

		private static readonly string StreamMetadata =
			string.Format("{{\"$maxAge\":{0}}}", (int)TimeSpan.FromDays(10).TotalSeconds);

		public static readonly TimeSpan MemoizePeriod = TimeSpan.FromSeconds(1);
		private static readonly IEnvelope NoopEnvelope = new NoopEnvelope();

		private readonly IQueuedHandler _monitoringQueue;
		private readonly IAsyncHandle<Message> _statsCollectionDispatcher;
		private readonly IPublisher _mainQueue;
		private readonly IReadOnlyCheckpoint _writerCheckpoint;
		private readonly string _dbPath;
		private readonly StatsStorage _statsStorage;
		private readonly TimeSpan _statsCollectionPeriod;
		private SystemStatsHelper _systemStats;

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
		private IPEndPoint _tcpEndpoint;
		private IPEndPoint _tcpSecureEndpoint;
		private Atomic.Boolean _started;

		public MonitoringService(IQueuedHandler monitoringQueue,
			IAsyncHandle<Message> statsCollectionDispatcher,
			IPublisher mainQueue,
			IReadOnlyCheckpoint writerCheckpoint,
			string dbPath,
			TimeSpan statsCollectionPeriod,
			EndPoint nodeEndpoint,
			StatsStorage statsStorage,
			IPEndPoint tcpEndpoint,
			IPEndPoint tcpSecureEndpoint,
			SystemStatsHelper systemStatsHelper) {
			Ensure.NotNull(monitoringQueue, "monitoringQueue");
			Ensure.NotNull(statsCollectionDispatcher, nameof(statsCollectionDispatcher));
			Ensure.NotNull(mainQueue, nameof(mainQueue));
			Ensure.NotNull(writerCheckpoint, "writerCheckpoint");
			Ensure.NotNullOrEmpty(dbPath, "dbPath");
			Ensure.NotNull(nodeEndpoint, "nodeEndpoint");

			_monitoringQueue = monitoringQueue;
			_statsCollectionDispatcher = statsCollectionDispatcher;
			_mainQueue = mainQueue;
			_writerCheckpoint = writerCheckpoint;
			_dbPath = dbPath;
			_statsStorage = statsStorage;
			_statsCollectionPeriod = statsCollectionPeriod > TimeSpan.Zero
				? statsCollectionPeriod
				: Timeout.InfiniteTimeSpan;
			_nodeStatsStream = string.Format("{0}-{1}", SystemStreams.StatsStreamPrefix, nodeEndpoint);
			_tcpEndpoint = tcpEndpoint;
			_tcpSecureEndpoint = tcpSecureEndpoint;
			_timerTokenSource = new();
			_timerToken = _timerTokenSource.Token;
			_timer = Task.CompletedTask;
			_systemStats = systemStatsHelper;
		}

		public void Handle(SystemMessage.SystemInit message) {
			_timer = CollectRegularStatsJob();
		}

		[AsyncMethodBuilder(typeof(SpawningAsyncTaskMethodBuilder))]
		private async Task CollectRegularStatsJob() {
			if (_started.FalseToTrue()) {
				_systemStats.Start();
			}

			while (!_timerToken.IsCancellationRequested) {
				await Task.Delay(_statsCollectionPeriod);
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
				await _statsCollectionDispatcher.HandleAsync(
					new MonitoringMessage.InternalStatsRequest(new StatsCollectorEnvelope(statsContainer)),
					token);
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
			var evnt = new Event(Guid.NewGuid(), SystemEventTypes.StatsCollection, true, data, null);
			var corrId = Guid.NewGuid();
			var msg = new ClientMessage.WriteEvents(corrId, corrId, NoopEnvelope, false, _nodeStatsStream,
				ExpectedVersion.Any, new[] {evnt}, SystemAccounts.System);
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
					_streamMetadataWriteCorrId, _streamMetadataWriteCorrId, new PublishEnvelope(_monitoringQueue),
					false, SystemStreams.MetastreamOf(_nodeStatsStream), ExpectedVersion.NoStream,
					new[] {new Event(Guid.NewGuid(), SystemEventTypes.StreamMetadata, true, metadata, null)},
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
					Log.Debug("Failed to create stats stream '{stream}'. Reason : {e}({message}). Retrying...",
						_nodeStatsStream, message.Result, message.Message);
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
				StatsContainer stats;
				if (!TryGetMemoizedStats(out stats)) {
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

				message.Envelope.ReplyWith(
					new MonitoringMessage.GetFreshStatsCompleted(success: selectedStats != null, stats: selectedStats));
			} catch (Exception ex) {
				Log.Error(ex, "Error on getting fresh stats");
			}
		}

		public void Handle(MonitoringMessage.GetFreshTcpConnectionStats message) {
			try {
				IMonitoredTcpConnection[] connections = null;
				if (!TryGetMemoizedTcpConnections(out connections)) {
					connections = TcpConnectionMonitor.Default.GetTcpConnectionStats();
					if (connections != null) {
						_memoizedTcpConnections = connections;
						_lastTcpConnectionsRequestTime = DateTime.UtcNow;
					}
				}

				List<MonitoringMessage.TcpConnectionStats> connStats = new List<MonitoringMessage.TcpConnectionStats>();
				foreach (var conn in connections) {
					var tcpConn = conn as TcpConnection;
					if (tcpConn != null) {
						var isExternalConnection = _tcpEndpoint != null && _tcpEndpoint.Port == tcpConn.LocalEndPoint.GetPort();
						connStats.Add(new MonitoringMessage.TcpConnectionStats {
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
					}

					var tcpConnSsl = conn as TcpConnectionSsl;
					if (tcpConnSsl != null) {
						var isExternalConnection = _tcpSecureEndpoint != null &&
						                           _tcpSecureEndpoint.Port == tcpConnSsl.LocalEndPoint.GetPort();
						connStats.Add(new MonitoringMessage.TcpConnectionStats {
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
					}
				}

				message.Envelope.ReplyWith(
					new MonitoringMessage.GetFreshTcpConnectionStatsCompleted(connStats)
				);
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

		private bool TryGetMemoizedTcpConnections(out IMonitoredTcpConnection[] connections) {
			if (_memoizedTcpConnections == null || DateTime.UtcNow - _lastTcpConnectionsRequestTime > MemoizePeriod) {
				connections = null;
				return false;
			}

			connections = _memoizedTcpConnections;
			return true;
		}
	}
}
