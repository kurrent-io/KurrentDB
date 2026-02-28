// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DotNext.Threading;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Index;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Services.Monitoring.Stats;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Core.TransactionLog.Checkpoint;
using KurrentDB.Core.TransactionLog.LogRecords;
using ILogger = Serilog.ILogger;

namespace KurrentDB.Core.Services.Storage;

public interface IIndexCommitterService<TStreamId> {
	ValueTask Init(long checkpointPosition, CancellationToken token);
	void Stop();
	ValueTask<long> GetCommitLastEventNumber(CommitLogRecord record, CancellationToken token);
	void AddPendingPrepare(long transactionPosition, IPrepareLogRecord<TStreamId>[] prepares, long postPosition);
	void AddPendingCommit(CommitLogRecord commit, long postPosition);
}

public abstract class IndexCommitterService {
	protected readonly ILogger Log = Serilog.Log.ForContext<IndexCommitterService>();
}

public class IndexCommitterService<TStreamId> : IndexCommitterService, IIndexCommitterService<TStreamId>,
	IMonitoredQueue,
	IHandle<SystemMessage.BecomeShuttingDown>,
	IHandle<ReplicationTrackingMessage.ReplicatedTo>,
	IHandle<StorageMessage.CommitChased>,
	IHandle<ClientMessage.MergeIndexes>,
	IThreadPoolWorkItem {
	private readonly IIndexCommitter<TStreamId> _indexCommitter;
	private readonly IPublisher _publisher;
	private readonly IReadOnlyCheckpoint _replicationCheckpoint;
	private readonly IReadOnlyCheckpoint _writerCheckpoint;
	private readonly ITableIndex _tableIndex;

	// cached to avoid ObjectDisposedException
	private readonly CancellationToken _stopToken;
	private CancellationTokenSource _stop;

	public string Name => _queueStats.Name;

	private readonly QueueStatsCollector _queueStats;
	private readonly ConcurrentQueueWrapper<StorageMessage.CommitChased> _replicatedQueue = new();
	private readonly ConcurrentDictionary<long, PendingTransaction> _pendingTransactions = new();
	private readonly SortedList<long, StorageMessage.CommitChased> _commitAcks = new();
	private readonly AsyncManualResetEvent _addMsgSignal = new(initialState: false);
	private readonly TimeSpan _waitTimeoutMs = TimeSpan.FromMilliseconds(100);
	private readonly TaskCompletionSource<object> _tcs = new();

	public Task Task => _tcs.Task;

	public IndexCommitterService(
		IIndexCommitter<TStreamId> indexCommitter,
		IPublisher publisher,
		IReadOnlyCheckpoint writerCheckpoint,
		IReadOnlyCheckpoint replicationCheckpoint,
		ITableIndex tableIndex,
		QueueStatsManager queueStatsManager) {
		_indexCommitter = Ensure.NotNull(indexCommitter);
		_publisher = Ensure.NotNull(publisher);
		_writerCheckpoint = Ensure.NotNull(writerCheckpoint);
		_replicationCheckpoint = Ensure.NotNull(replicationCheckpoint);
		_tableIndex = tableIndex;
		_queueStats = queueStatsManager.CreateQueueStatsCollector("Index Committer");
		_stop = new();
		_stopToken = _stop.Token;
	}

	public async ValueTask Init(long chaserCheckpoint, CancellationToken token) {
		await _indexCommitter.Init(chaserCheckpoint, token);
		_publisher.Publish(new ReplicationTrackingMessage.IndexedTo(_indexCommitter.LastIndexedPosition));
		ThreadPool.UnsafeQueueUserWorkItem(this, preferLocal: false);
	}

	public void Stop() {
		if (Interlocked.Exchange(ref _stop, null) is { } cts) {
			using (cts) {
				cts.Cancel();
			}
		}
	}

	async void IThreadPoolWorkItem.Execute() {
		_publisher.Publish(new SystemMessage.ServiceInitialized(nameof(IndexCommitterService)));
		try {
			_queueStats.Start();
			QueueMonitor.Default.Register(this);

			StorageMessage.CommitChased replicatedMessage;
			var msgType = typeof(StorageMessage.CommitChased);
			while (!_stopToken.IsCancellationRequested) {
				_addMsgSignal.Reset();
				if (_replicatedQueue.TryDequeue(out replicatedMessage)) {
					_queueStats.EnterBusy();
#if DEBUG
					_queueStats.Dequeued(replicatedMessage);
#endif
					_queueStats.ProcessingStarted(msgType, _replicatedQueue.Count);
					await ProcessCommitReplicated(replicatedMessage, _stopToken);
					_queueStats.ProcessingEnded(1);
				} else {
					_queueStats.EnterIdle();
					await _addMsgSignal.WaitAsync(_waitTimeoutMs, _stopToken);
				}
			}
		} catch (OperationCanceledException exc) when (exc.CancellationToken == _stopToken) {
			// shutdown gracefully on cancellation
		} catch (Exception exc) {
			_queueStats.EnterIdle();
			_queueStats.ProcessingStarted<FaultedIndexCommitterServiceState>(0);
			Log.Fatal(exc, "Error in IndexCommitterService. Terminating...");
			_tcs.TrySetException(exc);
			Application.Exit(ExitCode.Error, "Error in IndexCommitterService. Terminating...\nError: " + exc.Message);

			await _stopToken.WaitAsync();

			_queueStats.ProcessingEnded(0);
		} finally {
			_queueStats.Stop();
			QueueMonitor.Default.Unregister(this);
		}

		_publisher.Publish(new SystemMessage.ServiceShutdown(nameof(IndexCommitterService)));
	}

	private async ValueTask ProcessCommitReplicated(StorageMessage.CommitChased message, CancellationToken token) {
		if (_pendingTransactions.TryRemove(message.TransactionPosition, out var transaction)) {
			var isTfEof = IsTfEof(transaction.PostPosition);
			if (transaction.Prepares.Count > 0) {
				await _indexCommitter.Commit(transaction.Prepares, isTfEof, true, token);
			} else if (isTfEof) {
				_publisher.Publish(new StorageMessage.IndexedToEndOfTransactionFile());
			}

			if (transaction.Commit is not null) {
				await _indexCommitter.Commit(transaction.Commit, isTfEof, true, token);
			}
		}

		_publisher.Publish(new ReplicationTrackingMessage.IndexedTo(message.LogPosition));

		_publisher.Publish(new StorageMessage.CommitIndexed(message.CorrelationId, message.LogPosition,
			message.TransactionPosition));
	}

	private bool IsTfEof(long postPosition) => postPosition == _writerCheckpoint.Read();

	public ValueTask<long> GetCommitLastEventNumber(CommitLogRecord commit, CancellationToken token)
		=> _indexCommitter.GetCommitLastEventNumber(commit, token);

	// Only called with complete implicit transactions
	public void AddPendingPrepare(long transactionPosition, IPrepareLogRecord<TStreamId>[] prepares, long postPosition) {
		var pendingTransaction = new PendingTransaction(transactionPosition, postPosition, prepares);
		if (!_pendingTransactions.TryAdd(transactionPosition, pendingTransaction))
			throw new InvalidOperationException($"A pending transaction already exists at position: {transactionPosition}");
	}

	public void AddPendingCommit(CommitLogRecord commit, long postPosition) {
		if (_pendingTransactions.TryGetValue(commit.TransactionPosition, out var transaction)) {
			var newTransaction = new PendingTransaction(commit.TransactionPosition, postPosition, transaction.Prepares, commit);
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

	public void Handle(SystemMessage.BecomeShuttingDown message) => Stop();

	public void Handle(StorageMessage.CommitChased message) {
		lock (_commitAcks) {
			_commitAcks.TryAdd(message.LogPosition, message);
		}

		EnqueueReplicatedCommits();
	}

	public void Handle(ReplicationTrackingMessage.ReplicatedTo message) => EnqueueReplicatedCommits();

	private void EnqueueReplicatedCommits() {
		var replicated = new List<StorageMessage.CommitChased>();
		lock (_commitAcks) {
			while (_commitAcks.Count > 0) {
				var ack = _commitAcks.Values[0];
				if (ack.LogPosition >= _replicationCheckpoint.Read()) { break; }

				replicated.Add(ack);
				_commitAcks.RemoveAt(0);
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

	private class FaultedIndexCommitterServiceState;

	internal class PendingTransaction {
		public readonly List<IPrepareLogRecord<TStreamId>> Prepares = [];
		private readonly CommitLogRecord _commit;

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
