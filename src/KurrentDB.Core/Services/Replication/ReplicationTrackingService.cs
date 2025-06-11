// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.TransactionLog.Checkpoint;
using Serilog;
using ILogger = Serilog.ILogger;

namespace KurrentDB.Core.Services.Replication;

public class ReplicationTrackingService :
	IHandle<SystemMessage.StateChangeMessage>,
	IHandle<SystemMessage.BecomeShuttingDown>,
	IHandle<SystemMessage.SystemInit>,
	IHandle<ReplicationTrackingMessage.ReplicaWriteAck>,
	IHandle<ReplicationTrackingMessage.WriterCheckpointFlushed>,
	IHandle<ReplicationTrackingMessage.LeaderReplicatedTo>,
	IHandle<SystemMessage.VNodeConnectionLost>,
	IHandle<ReplicationMessage.ReplicaSubscribed> {

	private readonly ILogger _log = Log.ForContext<ReplicationTrackingService>();
	private readonly IPublisher _publisher;
	private readonly ICheckpoint _replicationCheckpoint;
	private readonly IReadOnlyCheckpoint _writerCheckpoint;
	private readonly int _quorumSize;
	private Thread _thread;
	private bool _stop;
	private VNodeState _state;
	private long _publishedPosition;
	private readonly ConcurrentDictionary<Guid, long> _replicaLogPositions = new();
	private readonly ManualResetEventSlim _replicationChange = new(false, 1);
	private readonly TaskCompletionSource<object> _tcs = new();

	public ReplicationTrackingService(
		IPublisher publisher,
		int clusterNodeCount,
		ICheckpoint replicationCheckpoint,
		IReadOnlyCheckpoint writerCheckpoint) {
		_publisher = Ensure.NotNull(publisher);
		_replicationCheckpoint = Ensure.NotNull(replicationCheckpoint);
		_writerCheckpoint = Ensure.NotNull(writerCheckpoint);
		_quorumSize = Ensure.Positive(clusterNodeCount) / 2 + 1;
	}

	public Task Task => _tcs.Task;

	public void Start() {
		_thread = new Thread(TrackReplication) { IsBackground = true, Name = nameof(ReplicationTrackingService) };
		_thread.Start();
	}

	public void Stop() {
		_stop = true;
	}

	public bool IsCurrent() {
		Debug.Assert(_state == VNodeState.Leader || _state == VNodeState.PreLeader);
		return Interlocked.Read(ref _publishedPosition) == _replicationCheckpoint.Read();
	}

	private void TrackReplication() {
		_publisher.Publish(new SystemMessage.ServiceInitialized(nameof(ReplicationTrackingService)));
		try {
			while (!_stop) {
				_replicationChange.Reset();
				if (_state == VNodeState.Leader || _state == VNodeState.PreLeader) {
					//Publish Log Commit Position
					var newPos = _replicationCheckpoint.Read();
					if (newPos > Interlocked.Read(ref _publishedPosition)) {
						_publisher.Publish(new ReplicationTrackingMessage.ReplicatedTo(newPos));
						Interlocked.Exchange(ref _publishedPosition, newPos);
					}
				}

				_replicationChange.Wait(100);
			}
		} catch (Exception exc) {
			_log.Fatal(exc, $"Error in {nameof(ReplicationTrackingService)}. Terminating...");
			_tcs.TrySetException(exc);
			Application.Exit(ExitCode.Error, $"Error in {nameof(ReplicationTrackingService)}. Terminating...\nError: {exc.Message}");
		}

		_publisher.Publish(new SystemMessage.ServiceShutdown(nameof(ReplicationTrackingService)));
	}

	public void Handle(ReplicationTrackingMessage.LeaderReplicatedTo message) {
		if (_stop)
			return;
		if (_state != VNodeState.Leader && _state != VNodeState.PreLeader && message.LogPosition > _replicationCheckpoint.Read()) {
			_replicationCheckpoint.Write(message.LogPosition);
			_replicationCheckpoint.Flush();
			_publisher.Publish(new ReplicationTrackingMessage.ReplicatedTo(message.LogPosition));
		}
	}


	private void UpdateReplicationPosition() {
		var replicationCp = _replicationCheckpoint.Read();
		var writerCp = _writerCheckpoint.Read();
		if (writerCp <= replicationCp) { return; }

		var minReplicas = _quorumSize - 1; //total - leader = min replicas
		if (minReplicas == 0) {
			_replicationCheckpoint.Write(writerCp);
			_replicationCheckpoint.Flush();
			_replicationChange.Set();
			return;
		}

		long[] positions;
		lock (_replicaLogPositions) {
			positions = _replicaLogPositions.Values.ToArray();
		}

		if (positions.Length < minReplicas) { return; }

		Array.Sort(positions);
		var furthestReplicatedPosition = positions[^minReplicas];
		if (furthestReplicatedPosition <= replicationCp) { return; }

		var newReplicationPoint = Math.Min(writerCp, furthestReplicatedPosition);
		_replicationCheckpoint.Write(newReplicationPoint);
		_replicationCheckpoint.Flush();
		_replicationChange.Set();
	}


	public void Handle(ReplicationTrackingMessage.ReplicaWriteAck message) {
		if (_state != VNodeState.Leader && _state != VNodeState.PreLeader) { return; }

		if (_replicaLogPositions.TryGetValue(message.SubscriptionId, out var position) &&
			message.ReplicationLogPosition <= position) { return; }

		_replicaLogPositions.AddOrUpdate(message.SubscriptionId, message.ReplicationLogPosition, (k, v) => message.ReplicationLogPosition);
		UpdateReplicationPosition();
	}

	public void Handle(ReplicationTrackingMessage.WriterCheckpointFlushed message) {
		if (_state != VNodeState.Leader && _state != VNodeState.PreLeader) { return; }

		UpdateReplicationPosition();
	}

	public void Handle(SystemMessage.StateChangeMessage msg) {
		if (msg is SystemMessage.BecomeLeader)
			Log.Error("ReplicationTracker");
		//switching to leader from non-leader
		if (_state != msg.State) {
			_replicaLogPositions.Clear();
		}

		_state = msg.State;
	}

	public void Handle(SystemMessage.VNodeConnectionLost msg) {
		if ((_state != VNodeState.Leader && _state != VNodeState.PreLeader) || !msg.SubscriptionId.HasValue)
			return;
		_replicaLogPositions.TryRemove(msg.SubscriptionId.Value, out _);
	}

	public void Handle(SystemMessage.BecomeShuttingDown message) {
		Stop();
	}

	public void Handle(SystemMessage.SystemInit message) {
		Start();
	}


	public void Handle(ReplicationMessage.ReplicaSubscribed message) {
		if (message.SubscriptionPosition < _writerCheckpoint.ReadNonFlushed()) {
			//Going offline for truncation
			_log.Information("Offline truncation will happen, shutting down {service}", nameof(ReplicationTrackingService));
			Stop();
		}
	}
}
