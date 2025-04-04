// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Common.Utils;
using EventStore.Core.Bus;
using EventStore.Core.Data;
using EventStore.Core.Index;
using EventStore.Core.Messages;
using EventStore.Core.Services.Monitoring.Stats;
using EventStore.Core.Services.Storage.ReaderIndex;
using EventStore.Core.TransactionLog.Checkpoint;
using EventStore.Core.TransactionLog.LogRecords;
using ILogger = Serilog.ILogger;


namespace EventStore.Core.Services.Storage;

public interface IIndexCommitterService<TStreamId> {
	void Init(long checkpointPosition);
	void Stop();
	long GetCommitLastEventNumber(CommitLogRecord record);
	void AddPendingPrepare(IPrepareLogRecord<TStreamId>[] prepares, long postPosition);
	void AddPendingCommit(CommitLogRecord commit, long postPosition);
}

public abstract class IndexCommitterService {
	protected readonly ILogger Log = Serilog.Log.ForContext<IndexCommitterService>();
}

public class IndexCommitterService<TStreamId> : IndexCommitterService, IIndexCommitterService<TStreamId>,
	IMonitoredQueue,
	IHandle<SystemMessage.BecomeShuttingDown>,
	IHandle<ReplicationTrackingMessage.ReplicatedTo>,
	IHandle<StorageMessage.CommitAck>,
	IHandle<ClientMessage.MergeIndexes> {
	private readonly IIndexCommitter<TStreamId> _indexCommitter;
	private readonly IPublisher _publisher;
	private readonly IReadOnlyCheckpoint _replicationCheckpoint;
	private readonly IReadOnlyCheckpoint _writerCheckpoint;
	private readonly ITableIndex _tableIndex;
	private Thread _thread;
	private bool _stop;

	public string Name {
		get { return _queueStats.Name; }
	}

	private readonly QueueStatsCollector _queueStats;

	private readonly ConcurrentQueueWrapper<StorageMessage.CommitAck> _replicatedQueue =
		new ConcurrentQueueWrapper<StorageMessage.CommitAck>();

	private readonly ConcurrentDictionary<long, PendingTransaction> _pendingTransactions =
		new ConcurrentDictionary<long, PendingTransaction>();

	private readonly SortedList<long, StorageMessage.CommitAck> _commitAcks = new SortedList<long, StorageMessage.CommitAck>();
	private readonly ManualResetEventSlim _addMsgSignal = new ManualResetEventSlim(false, 1);
	private TimeSpan _waitTimeoutMs = TimeSpan.FromMilliseconds(100);
	private readonly TaskCompletionSource<object> _tcs = new TaskCompletionSource<object>();

	public Task Task {
		get { return _tcs.Task; }
	}

	public IndexCommitterService(
		IIndexCommitter<TStreamId> indexCommitter,
		IPublisher publisher,
		IReadOnlyCheckpoint writerCheckpoint,
		IReadOnlyCheckpoint replicationCheckpoint,
		ITableIndex tableIndex,
		QueueStatsManager queueStatsManager) {
		Ensure.NotNull(indexCommitter, nameof(indexCommitter));
		Ensure.NotNull(publisher, nameof(publisher));
		Ensure.NotNull(writerCheckpoint, nameof(writerCheckpoint));
		Ensure.NotNull(replicationCheckpoint, nameof(replicationCheckpoint));


		_indexCommitter = indexCommitter;
		_publisher = publisher;
		_writerCheckpoint = writerCheckpoint;
		_replicationCheckpoint = replicationCheckpoint;
		_tableIndex = tableIndex;
		_queueStats = queueStatsManager.CreateQueueStatsCollector("Index Committer");
	}

	public void Init(long chaserCheckpoint) {
		_indexCommitter.Init(chaserCheckpoint);
		_publisher.Publish(new ReplicationTrackingMessage.IndexedTo(_indexCommitter.LastIndexedPosition));
		_thread = new Thread(HandleReplicatedQueue);
		_thread.IsBackground = true;
		_thread.Name = Name;
		_thread.Start();
	}

	public void Stop() {
		_stop = true;
	}

	public void HandleReplicatedQueue() {
		try {
			_queueStats.Start();
			QueueMonitor.Default.Register(this);

			StorageMessage.CommitAck replicatedMessage;
			var msgType = typeof(StorageMessage.CommitAck);
			while (!_stop) {
				_addMsgSignal.Reset();
				if (_replicatedQueue.TryDequeue(out replicatedMessage)) {
					_queueStats.EnterBusy();
#if DEBUG
					_queueStats.Dequeued(replicatedMessage);
#endif
					_queueStats.ProcessingStarted(msgType, _replicatedQueue.Count);
					ProcessCommitReplicated(replicatedMessage);
					_queueStats.ProcessingEnded(1);
				} else {
					_queueStats.EnterIdle();
					_addMsgSignal.Wait(_waitTimeoutMs);
				}
			}
		} catch (Exception exc) {
			_queueStats.EnterIdle();
			_queueStats.ProcessingStarted<FaultedIndexCommitterServiceState>(0);
			Log.Fatal(exc, "Error in IndexCommitterService. Terminating...");
			_tcs.TrySetException(exc);
			Application.Exit(ExitCode.Error,
				"Error in IndexCommitterService. Terminating...\nError: " + exc.Message);
			while (!_stop) {
				Thread.Sleep(100);
			}

			_queueStats.ProcessingEnded(0);
		} finally {
			_queueStats.Stop();
			QueueMonitor.Default.Unregister(this);
		}

		_publisher.Publish(new SystemMessage.ServiceShutdown(Name));
	}

	private void ProcessCommitReplicated(StorageMessage.CommitAck message) {
		PendingTransaction transaction;
		long lastEventNumber = message.LastEventNumber;
		if (_pendingTransactions.TryRemove(message.TransactionPosition, out transaction)) {
			var isTfEof = IsTfEof(transaction.PostPosition);
			if (transaction.Prepares.Count > 0) {
				_indexCommitter.Commit(transaction.Prepares, isTfEof, true);
			}

			if (transaction.Commit != null) {
				lastEventNumber = _indexCommitter.Commit(transaction.Commit, isTfEof, true);
			}
		}

		lastEventNumber = lastEventNumber == EventNumber.Invalid ? message.LastEventNumber : lastEventNumber;

		_publisher.Publish(new ReplicationTrackingMessage.IndexedTo(message.LogPosition));

		_publisher.Publish(new StorageMessage.CommitIndexed(message.CorrelationId, message.LogPosition,
			message.TransactionPosition, message.FirstEventNumber, lastEventNumber));
	}

	private bool IsTfEof(long postPosition) {
		return postPosition == _writerCheckpoint.Read();
	}

	public long GetCommitLastEventNumber(CommitLogRecord commit) {
		return _indexCommitter.GetCommitLastEventNumber(commit);
	}

	public void AddPendingPrepare(IPrepareLogRecord<TStreamId>[] prepares, long postPosition) {
		var transactionPosition = prepares[0].TransactionPosition;
		PendingTransaction transaction;
		if (_pendingTransactions.TryGetValue(transactionPosition, out transaction)) {
			var newTransaction = new PendingTransaction(transactionPosition, postPosition, transaction.Prepares,
				transaction.Commit);
			newTransaction.AddPendingPrepares(prepares);
			if (!_pendingTransactions.TryUpdate(transactionPosition, newTransaction, transaction)) {
				throw new InvalidOperationException("Failed to update pending prepare");
			}
		} else {
			var pendingTransaction = new PendingTransaction(transactionPosition, postPosition, prepares);
			if (!_pendingTransactions.TryAdd(transactionPosition, pendingTransaction)) {
				throw new InvalidOperationException("Failed to add pending prepare");
			}
		}
	}

	public void AddPendingCommit(CommitLogRecord commit, long postPosition) {
		PendingTransaction transaction;
		if (_pendingTransactions.TryGetValue(commit.TransactionPosition, out transaction)) {
			var newTransaction = new PendingTransaction(commit.TransactionPosition, postPosition,
				transaction.Prepares, commit);
			if (!_pendingTransactions.TryUpdate(commit.TransactionPosition, newTransaction, transaction)) {
				throw new InvalidOperationException("Failed to update pending commit");
			}
		} else {
			var pendingTransaction = new PendingTransaction(commit.TransactionPosition, postPosition, commit);
			if (!_pendingTransactions.TryAdd(commit.TransactionPosition, pendingTransaction)) {
				throw new InvalidOperationException("Failed to add pending commit");
			}
		}
	}

	public void Handle(SystemMessage.BecomeShuttingDown message) {
		_stop = true;
	}
	public void Handle(StorageMessage.CommitAck message) {
		lock (_commitAcks) {
			_commitAcks.TryAdd(message.LogPosition, message);
		}
		EnqueueReplicatedCommits();
	}

	public void Handle(ReplicationTrackingMessage.ReplicatedTo message) {
		EnqueueReplicatedCommits();
	}

	private void EnqueueReplicatedCommits() {
		var replicated = new List<StorageMessage.CommitAck>();
		lock (_commitAcks) {
			if (_commitAcks.Count > 0) {
				do {
					var ack = _commitAcks.Values[0];
					if (ack.LogPosition >= _replicationCheckpoint.Read()) { break; }
					replicated.Add(ack);
					_commitAcks.RemoveAt(0);
				} while (_commitAcks.Count > 0);
			}
		}
		foreach (var ack in replicated) {
#if DEBUG
			_queueStats.Enqueued();
#endif
			_replicatedQueue.Enqueue(ack);
			_addMsgSignal.Set();
		}
	}

	public QueueStats GetStatistics() {
		return _queueStats.GetStatistics(0);
	}

	private class FaultedIndexCommitterServiceState {
	}

	internal class PendingTransaction {
		public readonly List<IPrepareLogRecord<TStreamId>> Prepares = new List<IPrepareLogRecord<TStreamId>>();
		private CommitLogRecord _commit;

		public CommitLogRecord Commit {
			get { return _commit; }
		}

		public readonly long TransactionPosition;
		public readonly long PostPosition;

		public PendingTransaction(long transactionPosition, long postPosition,
			IEnumerable<IPrepareLogRecord<TStreamId>> prepares, CommitLogRecord commit = null) {
			TransactionPosition = transactionPosition;
			PostPosition = postPosition;
			Prepares.AddRange(prepares);
			_commit = commit;
		}

		public PendingTransaction(long transactionPosition, long postPosition, CommitLogRecord commit) {
			TransactionPosition = transactionPosition;
			PostPosition = postPosition;
			_commit = commit;
		}

		public void AddPendingPrepares(IEnumerable<IPrepareLogRecord<TStreamId>> prepares) {
			Prepares.AddRange(prepares);
		}

		public void SetPendingCommit(CommitLogRecord commit) {
			_commit = commit;
		}
	}

	public void Handle(ClientMessage.MergeIndexes message) {
		if (_tableIndex.IsBackgroundTaskRunning) {
			Log.Information("A background operation is already running...");
			MakeReplyForMergeIndexes(message);
			return;
		}

		_tableIndex.MergeIndexes();
		MakeReplyForMergeIndexes(message);
	}

	private static void MakeReplyForMergeIndexes(ClientMessage.MergeIndexes message) {
		message.Envelope.ReplyWith(new ClientMessage.MergeIndexesResponse(message.CorrelationId,
			ClientMessage.MergeIndexesResponse.MergeIndexesResult.Started));
	}
}
