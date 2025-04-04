using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Common.Utils;
using EventStore.Core.Bus;
using EventStore.Core.Data;
using EventStore.Core.Exceptions;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Services.Monitoring.Stats;
using EventStore.Core.Services.Storage.EpochManager;
using EventStore.Core.Services.Transport.Tcp;
using EventStore.Core.TransactionLog.Chunks;
using EventStore.Core.TransactionLog.Chunks.TFChunk;
using EventStore.Core.TransactionLog.LogRecords;
using EventStore.Transport.Tcp;
using ILogger = Serilog.ILogger;

namespace EventStore.Core.Services.Replication {
	public static class ReplicationSubscriptionVersions {
		// original
		public const int V0 = 0;

		// explicit writer checkpoint in acks
		public const int V1 = 1;
	}

	public class LeaderReplicationService : IMonitoredQueue,
		IHandle<SystemMessage.SystemStart>,
		IHandle<SystemMessage.StateChangeMessage>,
		IHandle<SystemMessage.EnablePreLeaderReplication>,
		IHandle<ReplicationMessage.ReplicaSubscriptionRequest>,
		IHandle<ReplicationMessage.ReplicaLogPositionAck>,
		IHandle<ReplicationMessage.GetReplicationStats>,
		IHandle<ReplicationTrackingMessage.ReplicatedTo> {
		public const int MaxQueueSize = 100;
		public const int CloneThreshold = 1024;
		public const int FollowerLagThreshold = 256 * 1024;
		public const int LagOccurencesThreshold = 2;
		public const int BulkSize = 8192;
		public const int ReplicaSendWindow = 16 * 1024 * 1024;
		public const int ReplicaAckWindow = 512 * 1024;
		public static readonly TimeSpan RoleAssignmentsInterval = TimeSpan.FromMilliseconds(1000);
		public static readonly TimeSpan NoQuorumTimeout = TimeSpan.FromMilliseconds(3000);

		private static readonly ILogger Log = Serilog.Log.ForContext<LeaderReplicationService>();

		public string Name {
			get { return _queueStats.Name; }
		}

		private readonly IPublisher _publisher;
		private readonly Guid _instanceId;
		private readonly TFChunkDb _db;
		private readonly IPublisher _tcpSendPublisher;
		private readonly IEpochManager _epochManager;
		private readonly int _clusterSize;
		private readonly bool _unsafeAllowSurplusNodes;

		private readonly Thread _mainLoopThread;
		private volatile bool _stop;
		private readonly QueueStatsCollector _queueStats;

		private readonly ConcurrentDictionary<Guid, ReplicaSubscription> _subscriptions =
			new ConcurrentDictionary<Guid, ReplicaSubscription>();

		private volatile VNodeState _state = VNodeState.Initializing;

		private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
		private TimeSpan _lastRolesAssignmentTimestamp;
		private volatile bool _newSubscriptions;
		private TimeSpan _noQuorumTimestamp = TimeSpan.Zero;
		private bool _noQuorumNotified;
		private bool _preLeaderReplicationEnabled;
		private ManualResetEventSlim _flushSignal = new ManualResetEventSlim(false, 1);
		private readonly TaskCompletionSource<object> _tcs = new TaskCompletionSource<object>();

		public Task Task {
			get { return _tcs.Task; }
		}

		public LeaderReplicationService(
			IPublisher publisher,
			Guid instanceId,
			TFChunkDb db,
			IPublisher tcpSendPublisher,
			IEpochManager epochManager,
			int clusterSize,
			bool unsafeAllowSurplusNodes,
			QueueStatsManager queueStatsManager) {
			Ensure.NotNull(publisher, "publisher");
			Ensure.NotEmptyGuid(instanceId, "instanceId");
			Ensure.NotNull(db, "db");
			Ensure.NotNull(tcpSendPublisher, "tcpSendPublisher");
			Ensure.NotNull(epochManager, "epochManager");
			Ensure.Positive(clusterSize, "clusterSize");

			_publisher = publisher;
			_instanceId = instanceId;
			_db = db;
			_tcpSendPublisher = tcpSendPublisher;
			_epochManager = epochManager;
			_clusterSize = clusterSize;
			_unsafeAllowSurplusNodes = unsafeAllowSurplusNodes;
			_queueStats = queueStatsManager.CreateQueueStatsCollector("Leader Replication Service");

			_lastRolesAssignmentTimestamp = _stopwatch.Elapsed;
			_mainLoopThread = new Thread(MainLoop) { Name = _queueStats.Name, IsBackground = true };
		}

		public void Handle(SystemMessage.SystemStart message) {
			_mainLoopThread.Start();
		}

		public void Handle(SystemMessage.StateChangeMessage message) {
			_state = message.State;

			if (message.State == VNodeState.PreLeader) {
				_preLeaderReplicationEnabled = false;
				_noQuorumTimestamp = TimeSpan.Zero;
			}

			if (message.State == VNodeState.Leader)
				_noQuorumTimestamp = TimeSpan.Zero;

			if (message.State == VNodeState.ShuttingDown)
				_stop = true;
		}

		public void Handle(SystemMessage.EnablePreLeaderReplication message) {
			_preLeaderReplicationEnabled = true;
		}

		private bool CanAcceptSubscription(ReplicationMessage.ReplicaSubscriptionRequest message) {
			return CanAcceptSubscriptions() && message.LeaderId == _instanceId;
		}

		private bool CanAcceptSubscriptions() {
			if (_state == VNodeState.Leader)
				return true;

			if (_state == VNodeState.PreLeader && _preLeaderReplicationEnabled)
				return true;

			return false;
		}

		public void Handle(ReplicationMessage.ReplicaSubscriptionRequest message) {
			_publisher.Publish(new SystemMessage.VNodeConnectionEstablished(message.ReplicaEndPoint,
				message.Connection.ConnectionId));

			if (!CanAcceptSubscription(message)) {
				message.Envelope.ReplyWith(
					new ReplicationMessage.ReplicaSubscriptionRetry(_instanceId, message.SubscriptionId));
				return;
			}

			var subscription = new ReplicaSubscription(_tcpSendPublisher,
				message.Version,
				message.Connection,
				message.SubscriptionId,
				message.ReplicaEndPoint,
				message.IsPromotable);

			foreach (var subscr in _subscriptions.Values) {
				if (subscr != subscription && subscr.ConnectionId == subscription.ConnectionId)
					subscr.ShouldDispose = true;
			}

			if (SubscribeReplica(subscription, message.LastEpochs, message.CorrelationId, message.LogPosition,
				message.ChunkId)) {
				_newSubscriptions = true;
				if (!_subscriptions.TryAdd(subscription.SubscriptionId, subscription)) {
					ReplicaSubscription existingSubscr;
					_subscriptions.TryGetValue(subscription.SubscriptionId, out existingSubscr);
					Log.Error(
						"There is already a subscription with SubscriptionID {subscriptionId:B}: {existingSubscription}.",
						subscription.SubscriptionId, existingSubscr);
					Log.Error("Subscription we tried to add: {existingSubscription}.", existingSubscr);
					subscription.SendBadRequestAndClose(message.CorrelationId, string.Format(
						"There is already a subscription with SubscriptionID {0:B}: {1}.\nSubscription we tried to add: {2}",
						subscription.SubscriptionId, existingSubscr, subscription));
					subscription.Dispose();
				} else {
					var replicationCheckpoint = _db.Config.ReplicationCheckpoint.Read();
					subscription.SendMessage(new ReplicationTrackingMessage.ReplicatedTo(replicationCheckpoint));
					_flushSignal.Set();
				}
			}
		}

		public void Handle(ReplicationMessage.ReplicaLogPositionAck message) {
			if (_subscriptions.TryGetValue(message.SubscriptionId, out var subscription)) {
				Interlocked.Exchange(ref subscription.AckedLogPosition, message.ReplicationLogPosition);
				if (subscription.IsPromotable) {
					var replicatedToLogPosition = subscription.Version == ReplicationSubscriptionVersions.V0
						? message.ReplicationLogPosition
						: message.WriterLogPosition;
					_publisher.Publish(new ReplicationTrackingMessage.ReplicaWriteAck(message.SubscriptionId, replicatedToLogPosition));
				}
			}
		}

		public void Handle(ReplicationMessage.GetReplicationStats message) {
			var connections = TcpConnectionMonitor.Default.GetTcpConnectionStats();
			var replicaStats = new List<ReplicationMessage.ReplicationStats>();
			foreach (var conn in connections) {
				var tcpConn = conn as ITcpConnection;
				if (tcpConn != null) {
					var subscription = _subscriptions.FirstOrDefault(x => x.Value.ConnectionId == tcpConn.ConnectionId);
					if (subscription.Value != null) {
						var stats = new ReplicationMessage.ReplicationStats(subscription.Key, tcpConn.ConnectionId,
							subscription.Value.ReplicaEndPoint.ToString(), tcpConn.SendQueueSize,
							conn.TotalBytesSent, conn.TotalBytesReceived, conn.PendingSendBytes,
							conn.PendingReceivedBytes);
						replicaStats.Add(stats);
					}
				}
			}

			message.Envelope.ReplyWith(new ReplicationMessage.GetReplicationStatsCompleted(replicaStats));
		}

		private bool SubscribeReplica(ReplicaSubscription replica, Epoch[] lastEpochs, Guid correlationId,
			long logPosition, Guid chunkId) {
			try {
				var epochs = lastEpochs ?? new Epoch[0];
				Log.Information(
					"SUBSCRIBE REQUEST from [{replicaEndPoint},V:{version},C:{connectionId:B},S:{subscriptionId:B},{logPosition}(0x{logPosition:X}),{epochs}]...",
					replica.ReplicaEndPoint, replica.Version, replica.ConnectionId, replica.SubscriptionId, logPosition, logPosition,
					string.Join(", ", epochs.Select(x => EpochRecordExtensions.AsString((Epoch)x))));

				var epochCorrectedLogPos =
					GetValidLogPosition(logPosition, epochs, replica.ReplicaEndPoint, replica.SubscriptionId);
				var subscriptionPos = SetSubscriptionPosition(replica, epochCorrectedLogPos, chunkId,
					replicationStart: true, verbose: true, trial: 0);
				Interlocked.Exchange(ref replica.AckedLogPosition, subscriptionPos);

				if (subscriptionPos != logPosition) {
					Log.Information("Replica is truncating. Sending it no more messages.");
					replica.IsTruncating = true;
				}

				return true;
			} catch (Exception exc) {
				Log.Error(exc, "Exception while subscribing replica. Connection will be dropped.");
				replica.SendBadRequestAndClose(correlationId,
					string.Format("Exception while subscribing replica. Connection will be dropped. Error: {0}",
						exc.Message));
				return false;
			}
		}

		private long GetValidLogPosition(long logPosition, Epoch[] epochs, EndPoint replicaEndPoint,
			Guid subscriptionId) {
			if (epochs.Length == 0) {
				if (logPosition > 0) {
					// follower has some data, but doesn't have any epoch
					// for now we'll just report error and close connection
					var msg = string.Format(
						"Replica [{0},S:{1},{2}] has positive LogPosition {3} (0x{3:X}), but does not have epochs.",
						replicaEndPoint, subscriptionId,
						string.Join(", ", epochs.Select(x => x.AsString())), logPosition);
					Log.Information(
						"Replica [{replicaEndPoint},S:{subscriptionId},{epochs}] has positive LogPosition {logPosition} (0x{logPosition:X}), but does not have epochs.",
						replicaEndPoint, subscriptionId,
						string.Join(", ", epochs.Select(x => x.AsString())), logPosition, logPosition);
					throw new Exception(msg);
				}

				return 0;
			}

			var leaderCheckpoint = _db.Config.WriterCheckpoint.Read();
			Epoch nextEpochAfterCommonEpoch = null;
			Epoch commonEpoch = null;
			for (int i = 0; i < epochs.Length; ++i) {
				var epoch = epochs[i];
				if (_epochManager.IsCorrectEpochAt(epoch.EpochPosition, epoch.EpochNumber, epoch.EpochId)) {
					commonEpoch = epoch;
					nextEpochAfterCommonEpoch = i > 0 ? epochs[i - 1] : null;
					break;
				}
			}

			if (commonEpoch == null) {
				Log.Error(
					"No common epoch found for replica [{replicaEndPoint},S{subscriptionId},{logPosition}(0x{logPosition:X}),{epochs}]. Subscribing at 0. Leader LogPosition: {leaderCheckpoint} (0x{leaderCheckpoint:X}), known epochs: {knownEpochs}.",
					replicaEndPoint, subscriptionId,
					logPosition, logPosition,
					string.Join(", ", epochs.Select(x => x.AsString())),
					leaderCheckpoint, leaderCheckpoint,
					string.Join(", ", _epochManager.GetLastEpochs(int.MaxValue).Select(x => x.AsString())));
				return 0;
			}

			// if nextEpochAfterCommonEpoch is present, replica logPosition > nextEpochAfterCommonEpoch.EpochPosition,
			// so safe position is definitely the start of nextEpochAfterCommonEpoch
			var replicaPosition = nextEpochAfterCommonEpoch?.EpochPosition ?? logPosition;

			if (commonEpoch.EpochNumber == _epochManager.LastEpochNumber)
				return Math.Min(replicaPosition, leaderCheckpoint);

			// common epoch number is older than the last epoch
			var nextEpoch = _epochManager.GetEpochAfter(commonEpoch.EpochNumber, false);

			if (nextEpoch == null) {
				var msg = string.Format(
					"Replica [{0},S:{1},{2}(0x{3:X}),epochs:\n{4}]\n provided epochs which are not in "
					+ "EpochManager (possibly too old, known epochs:\n{5}).\nLeader LogPosition: {6} (0x{7:X}). "
					+ "We do not support this case as of now.\n"
					+ "CommonEpoch: {8}, NextEpochAfterCommonEpoch: {9}",
					replicaEndPoint, subscriptionId, logPosition, logPosition,
					string.Join("\n", epochs.Select(x => x.AsString())),
					string.Join("\n", _epochManager.GetLastEpochs(int.MaxValue).Select(x => x.AsString())),
					leaderCheckpoint, leaderCheckpoint,
					commonEpoch.AsString(), nextEpochAfterCommonEpoch == null ? "<none>" : nextEpochAfterCommonEpoch.AsString());
				Log.Error(
					"Replica [{replicaEndPoint},S:{subscriptionId},{logPosition}(0x{logPosition:X}),epochs:\n{epochs}]\n provided epochs which are not in "
					+ "EpochManager (possibly too old, known epochs:\n{lastEpochs}).\nLeader LogPosition: {leaderCheckpoint} (0x{leaderCheckpoint:X}). "
					+ "We do not support this case as of now.\n"
					+ "CommonEpoch: {commonEpoch}, NextEpochAfterCommonEpoch: {nextEpochAfterCommonEpoch}",
					replicaEndPoint,
					subscriptionId,
					logPosition,
					logPosition,
					string.Join("\n", epochs.Select(x => x.AsString())),
					string.Join("\n", _epochManager.GetLastEpochs(int.MaxValue).Select(x => x.AsString())),
					leaderCheckpoint,
					leaderCheckpoint,
					commonEpoch.AsString(),
					nextEpochAfterCommonEpoch == null ? "<none>" : nextEpochAfterCommonEpoch.AsString()
				);
				throw new Exception(msg);
			}

			return Math.Min(replicaPosition, nextEpoch.EpochPosition);
		}

		private long SetSubscriptionPosition(ReplicaSubscription sub,
			long logPosition,
			Guid chunkId,
			bool replicationStart,
			bool verbose,
			int trial) {
			if (trial >= 10)
				throw new Exception("Too many retrials to acquire reader for subscriber.");

			try {
				var chunk = _db.Manager.GetChunkFor(logPosition);
				Debug.Assert(chunk != null, string.Format(
					"Chunk for LogPosition {0} (0x{0:X}) is null in LeaderReplicationService! Replica: [{1},C:{2},S:{3}]",
					logPosition, sub.ReplicaEndPoint, sub.ConnectionId, sub.SubscriptionId));
				var bulkReader = chunk.AcquireReader();
				if (chunk.ChunkHeader.IsScavenged && (chunkId == Guid.Empty || chunkId != chunk.ChunkHeader.ChunkId)) {
					var chunkStartPos = chunk.ChunkHeader.ChunkStartPosition;
					if (verbose) {
						Log.Information(
							"Subscribed replica [{replicaEndPoint}, S:{subscriptionId}] for raw send at {chunkStartPosition} (0x{chunkStartPosition:X}) (requested {logPosition} (0x{logPosition:X})).",
							sub.ReplicaEndPoint, sub.SubscriptionId, chunkStartPos, chunkStartPos, logPosition,
							logPosition);
						if (chunkStartPos != logPosition) {
							Log.Information(
								"Forcing replica [{replicaEndPoint}, S:{subscriptionId}] to recreate chunk from position {chunkStartPosition} (0x{chunkStartPosition:X})...",
								sub.ReplicaEndPoint, sub.SubscriptionId, chunkStartPos, chunkStartPos);
						}
					}

					sub.LogPosition = chunkStartPos;
					sub.RawSend = true;
					bulkReader.SetRawPosition(ChunkHeader.Size);
					if (replicationStart)
						sub.SendMessage(new ReplicationMessage.ReplicaSubscribed(_instanceId, sub.SubscriptionId,
							sub.LogPosition));
					sub.SendMessage(new ReplicationMessage.CreateChunk(_instanceId,
						sub.SubscriptionId,
						chunk.ChunkHeader,
						chunk.FileSize,
						isCompletedChunk: true));
				} else {
					if (verbose)
						Log.Information(
							"Subscribed replica [{replicaEndPoint},S:{subscriptionId}] for data send at {logPosition} (0x{logPosition:X}).",
							sub.ReplicaEndPoint, sub.SubscriptionId, logPosition, logPosition);

					sub.LogPosition = logPosition;
					sub.RawSend = false;
					bulkReader.SetDataPosition(chunk.ChunkHeader.GetLocalLogPosition(logPosition));
					if (replicationStart)
						sub.SendMessage(new ReplicationMessage.ReplicaSubscribed(_instanceId, sub.SubscriptionId,
							sub.LogPosition));
				}

				sub.EOFSent = false;
				var oldBulkReader = Interlocked.Exchange(ref sub.BulkReader, bulkReader);
				if (oldBulkReader != null)
					oldBulkReader.Release();
				return sub.LogPosition;
			} catch (FileBeingDeletedException) {
				return SetSubscriptionPosition(sub, logPosition, chunkId, replicationStart, verbose, trial + 1);
			}
		}

		private void MainLoop() {
			try {
				_queueStats.Start();
				QueueMonitor.Default.Register(this);

				_db.Config.WriterCheckpoint.Flushed += OnWriterFlushed;

				while (!_stop) {
					try {
						_queueStats.EnterBusy();

						_queueStats.ProcessingStarted(typeof(SendReplicationData), _subscriptions.Count);

						_flushSignal
							.Reset(); // Reset the flush signal as we're about to read anyway. This could be closer to the actual read but no harm from too many checks.

						var dataFound = ManageSubscriptions();
						ManageNoQuorumDetection();
						var newSubscriptions = _newSubscriptions;
						_newSubscriptions = false;
						ManageRoleAssignments(force: newSubscriptions);

						_queueStats.ProcessingEnded(_subscriptions.Count);

						if (!dataFound) {
							_queueStats.EnterIdle();

							_flushSignal.Wait(TimeSpan.FromMilliseconds(500));
						}
					} catch (Exception exc) {
						Log.Information(exc, "Error during leader replication iteration.");
#if DEBUG
						throw;
#endif
					}
				}

				foreach (var subscription in _subscriptions.Values) {
					subscription.Dispose();
				}

				_db.Config.WriterCheckpoint.Flushed -= OnWriterFlushed;

				_publisher.Publish(new SystemMessage.ServiceShutdown(Name));
			} catch (Exception ex) {
				_tcs.TrySetException(ex);
				throw;
			} finally {
				_queueStats.Stop();
				QueueMonitor.Default.Unregister(this);
			}
		}

		private void OnWriterFlushed(long obj) {
			_flushSignal.Set();
		}

		private bool ManageSubscriptions() {
			var dataFound = false;
			foreach (var subscription in _subscriptions.Values) {
				bool lost = false;
				if (subscription.IsConnectionClosed) {
					subscription.ShouldDispose = true;
					lost = true;
				}

				if (subscription.ShouldDispose) {
					_subscriptions.TryRemove(subscription.SubscriptionId, out _);
					if (lost) {
						_publisher.Publish(new SystemMessage.VNodeConnectionLost(subscription.ReplicaEndPoint,
							subscription.ConnectionId, subscription.SubscriptionId));
					}

					subscription.Dispose();
					continue;
				}

				if (subscription.SendQueueSize >= MaxQueueSize
					|| subscription.LogPosition - Interlocked.Read(ref subscription.AckedLogPosition) >=
					ReplicaSendWindow)
					continue;

				if (subscription.BulkReader == null)
					throw new Exception("BulkReader is null for subscription.");

				try {
					var leaderCheckpoint = _db.Config.WriterCheckpoint.Read();

					if (TrySendLogBulk(subscription, leaderCheckpoint))
						dataFound = true;

					if (subscription.State == ReplicaState.CatchingUp &&
						leaderCheckpoint - subscription.LogPosition <= CloneThreshold) {
						subscription.State = ReplicaState.Clone;
						subscription.SendMessage(
							new ReplicationMessage.CloneAssignment(_instanceId, subscription.SubscriptionId));
					}
				} catch (Exception exc) {
					Log.Information(exc, "Error during replication send to replica: {subscription}.", subscription);
				}
			}

			return dataFound;
		}

		private bool TrySendLogBulk(ReplicaSubscription subscription, long leaderCheckpoint) {
			/*
			if (subscription == null) throw new Exception("subscription == null");
			if (subscription.BulkReader == null) throw new Exception("subscription.BulkReader == null");
			if (subscription.BulkReader.Chunk == null) throw new Exception("subscription.BulkReader.Chunk == null");
			if (subscription.DataBuffer == null) throw new Exception("subscription.DataBuffer == null");
			*/

			var bulkReader = subscription.BulkReader;
			var chunkHeader = bulkReader.Chunk.ChunkHeader;

			BulkReadResult bulkResult;
			if (subscription.RawSend) {
				bulkResult = bulkReader.ReadNextRawBytes(subscription.DataBuffer.Length, subscription.DataBuffer);
			} else {
				var bytesToRead = (int)Math.Min(subscription.DataBuffer.Length,
					leaderCheckpoint - subscription.LogPosition);
				bulkResult = bulkReader.ReadNextDataBytes(bytesToRead, subscription.DataBuffer);
			}

			bool dataFound = false;
			// for logical send we can get 0 at the end multiple time, but we need to get EOF exactly once
			if (bulkResult.BytesRead > 0 || (bulkResult.IsEOF && !subscription.RawSend && !subscription.EOFSent)) {
				var data = new byte[bulkResult.BytesRead];
				Buffer.BlockCopy(subscription.DataBuffer, 0, data, 0, bulkResult.BytesRead);

				dataFound = true;
				subscription.EOFSent = bulkResult.IsEOF;

				if (subscription.RawSend) {
					var msg = new ReplicationMessage.RawChunkBulk(
						_instanceId, subscription.SubscriptionId, chunkHeader.ChunkStartNumber,
						chunkHeader.ChunkEndNumber,
						bulkResult.OldPosition, data, bulkResult.IsEOF);
					subscription.SendMessage(msg);
				} else {
					if (chunkHeader.GetLocalLogPosition(subscription.LogPosition) != bulkResult.OldPosition) {
						throw new Exception(string.Format(
							"Replication invariant failure. SubscriptionPosition {0}, bulkResult.OldPosition {1}",
							subscription.LogPosition, bulkResult.OldPosition));
					}

					var msg = new ReplicationMessage.DataChunkBulk(
						_instanceId, subscription.SubscriptionId, chunkHeader.ChunkStartNumber,
						chunkHeader.ChunkEndNumber,
						subscription.LogPosition, data, bulkResult.IsEOF);
					subscription.LogPosition += bulkResult.BytesRead;
					subscription.SendMessage(msg);
				}
			}

			if (bulkResult.IsEOF) {
				var newLogPosition = chunkHeader.ChunkEndPosition;
				if (newLogPosition < leaderCheckpoint) {
					dataFound = true;
					SetSubscriptionPosition(subscription, newLogPosition, Guid.Empty, replicationStart: false,
						verbose: true, trial: 0);
				}
			}

			return dataFound;
		}

		private void ManageNoQuorumDetection() {
			if (CanAcceptSubscriptions()) {
				var now = _stopwatch.Elapsed;
				if (_subscriptions.Count(x => x.Value.IsPromotable) >= _clusterSize / 2) // everything is ok
					_noQuorumTimestamp = TimeSpan.Zero;
				else {
					if (_noQuorumTimestamp == TimeSpan.Zero) {
						_noQuorumTimestamp = now;
						_noQuorumNotified = false;
					}

					if (!_noQuorumNotified && now - _noQuorumTimestamp > NoQuorumTimeout) {
						_publisher.Publish(new SystemMessage.NoQuorumMessage());
						_noQuorumNotified = true;
					}
				}
			}
		}

		private void ManageRoleAssignments(bool force = false) {
			if (force || _stopwatch.Elapsed - _lastRolesAssignmentTimestamp >= RoleAssignmentsInterval) {
				ManageRoleAssignments(_subscriptions.Values);
				_lastRolesAssignmentTimestamp = _stopwatch.Elapsed;
			}
		}

		private void ManageRoleAssignments(IEnumerable<ReplicaSubscription> subscribers) {
			var candidates = subscribers.Where(x => x.IsPromotable && x.State != ReplicaState.CatchingUp)
				.OrderByDescending(x => x.LogPosition)
				.ToArray();
			var leaderCheckpoint = _db.Config.WriterCheckpoint.Read();

			int followerCount = 0;
			int laggedFollowers = 0;
			var desiredFollowerCount = _clusterSize - 1;

			for (int i = 0; i < candidates.Length; ++i) {
				var candidate = candidates[i];
				if (candidate.State == ReplicaState.Follower) {
					followerCount++;
					candidate.LagOccurences = i < desiredFollowerCount ? 0 : candidate.LagOccurences + 1;

					if (candidate.LagOccurences >= LagOccurencesThreshold
						&& leaderCheckpoint - candidate.LogPosition >= FollowerLagThreshold) {
						++laggedFollowers;
					}
				}
			}

			int cloneIndex = 0;
			int followerIndex = candidates.Length - 1;
			for (int k = followerCount; k < desiredFollowerCount; ++k) {
				// find next best clone
				while (cloneIndex < candidates.Length && candidates[cloneIndex].State != ReplicaState.Clone)
					cloneIndex++;

				// out of suitable clones - get out of here
				if (cloneIndex >= candidates.Length)
					break;

				// we need more followers, even if there are lagging followers
				var newFollower = candidates[cloneIndex];
				newFollower.State = ReplicaState.Follower;
				newFollower.LagOccurences = 0;
				newFollower.SendMessage(new ReplicationMessage.FollowerAssignment(_instanceId, newFollower.SubscriptionId));
				cloneIndex++;
			}

			for (int k = 0; k < laggedFollowers; ++k) {
				// find next best clone
				while (cloneIndex < candidates.Length && candidates[cloneIndex].State != ReplicaState.Clone)
					cloneIndex++;

				// find next worst follower
				while (followerIndex >= 0 && candidates[followerIndex].State != ReplicaState.Follower)
					followerIndex--;

				// no more suitable clones - get out of here
				if (cloneIndex > followerIndex)
					break;

				// we have enough follower, but some of them are probably lagging behind
				Debug.Assert(followerIndex >= 0);

				var oldFollower = candidates[followerIndex];
				oldFollower.State = ReplicaState.Clone;
				oldFollower.LagOccurences = 0;
				oldFollower.SendMessage(new ReplicationMessage.CloneAssignment(_instanceId, oldFollower.SubscriptionId));
				followerIndex--;

				var newFollower = candidates[cloneIndex];
				newFollower.State = ReplicaState.Follower;
				newFollower.LagOccurences = 0;
				newFollower.SendMessage(new ReplicationMessage.FollowerAssignment(_instanceId, newFollower.SubscriptionId));
				cloneIndex++;
			}

			//drop surplus of clones unless the unsafe option is enabled
			if (!_unsafeAllowSurplusNodes) {
				while (cloneIndex < candidates.Length && candidates[cloneIndex].State != ReplicaState.Clone) {
					cloneIndex++;
				}

				while (cloneIndex < candidates.Length && candidates[cloneIndex].State == ReplicaState.Clone) {
					var cloneToDrop = candidates[cloneIndex];
					cloneToDrop.SendMessage(
						new ReplicationMessage.DropSubscription(_instanceId, cloneToDrop.SubscriptionId));
					Log.Debug(
						"There is a surplus of nodes in the cluster. Dropped clone: C:{connectionId:B}, S:{subscriptionId:B}.",
						cloneToDrop.ConnectionId, cloneToDrop.SubscriptionId);

					cloneIndex++;
				}
			}
		}
		public void Handle(ReplicationTrackingMessage.ReplicatedTo message) {
			//TODO(clc): if the node is busy and misses an update it might be a long time till the next update do we need check if they get too stale?
			foreach (var subscription in _subscriptions.Values) {
				if (subscription.IsConnectionClosed || subscription.SendQueueSize >= MaxQueueSize) { continue; }
				subscription.SendMessage(message);
			}
		}
		public QueueStats GetStatistics() {
			return _queueStats.GetStatistics(_subscriptions.Count);
		}

		private enum ReplicaState {
			CatchingUp,
			Clone,
			Follower
		}

		private class SendReplicationData {
		}

		private class ReplicaSubscription : IDisposable {
			public readonly byte[] DataBuffer = new byte[BulkSize];

			public Guid ConnectionId {
				get { return _connection.ConnectionId; }
			}

			public int SendQueueSize {
				get { return _connection.SendQueueSize; }
			}

			public bool IsConnectionClosed {
				get { return _connection.IsClosed; }
			}

			public readonly int Version;
			public readonly bool IsPromotable;
			public readonly EndPoint ReplicaEndPoint;
			public readonly Guid SubscriptionId;

			public TFChunkBulkReader BulkReader;
			public bool RawSend;

			public bool EOFSent;
			public long LogPosition;
			public long AckedLogPosition;

			public bool ShouldDispose;
			public bool IsTruncating;
			public ReplicaState State = ReplicaState.CatchingUp;
			public int LagOccurences;

			private readonly IPublisher _tcpSendPublisher;
			private readonly TcpConnectionManager _connection;

			public ReplicaSubscription(IPublisher tcpSendPublisher,
				int version,
				TcpConnectionManager connection,
				Guid subscriptionId, EndPoint replicaEndPoint, bool isPromotable) {
				_tcpSendPublisher = tcpSendPublisher;
				Version = version;
				_connection = connection;
				SubscriptionId = subscriptionId;
				ReplicaEndPoint = replicaEndPoint;
				IsPromotable = isPromotable;
			}

			public void SendMessage(Message msg) {
				if (IsTruncating)
					return;

				_tcpSendPublisher.Publish(new TcpMessage.TcpSend(_connection, msg));
			}

			public void SendBadRequestAndClose(Guid correlationId, string message) {
				_connection.SendBadRequestAndClose(correlationId, message);
			}

			public override string ToString() {
				return string.Format(
					"Connection: {0:B}, ReplicaEndPoint: {1}, IsPromotable: {2}, RawSend: {3}, EOFSent: {4}, "
					+ "LogPosition: {5} (0x{5:X}), AckedLogPosition: {6} (0x{6:X}), State: {7}, LagOccurences: {8}, "
					+ "SubscriptionId: {9}, ShouldDispose: {10}",
					_connection.ConnectionId,
					ReplicaEndPoint,
					IsPromotable,
					RawSend,
					EOFSent,
					LogPosition,
					AckedLogPosition,
					State,
					LagOccurences,
					SubscriptionId,
					ShouldDispose);
			}

			public void Dispose() {
				_connection?.Stop("Closing replication subscription connection.");
				var bulkReader = Interlocked.Exchange(ref BulkReader, null);
				if (bulkReader != null)
					bulkReader.Release();
			}
		}
	}
}
