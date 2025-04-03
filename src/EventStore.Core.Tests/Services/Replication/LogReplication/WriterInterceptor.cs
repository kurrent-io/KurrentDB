// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Concurrent;
using System.Collections.Generic;
using EventStore.Core.Bus;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;

namespace EventStore.Core.Tests.Services.Replication.LogReplication;

internal class WriterInterceptor:
	IHandle<SystemMessage.SystemInit>,
	IHandle<SystemMessage.StateChangeMessage>,
	IHandle<SystemMessage.WriteEpoch>,
	IHandle<SystemMessage.WaitForChaserToCatchUp>,
	IHandle<StorageMessage.WritePrepares>,
	IHandle<StorageMessage.WriteDelete>,
	IHandle<StorageMessage.WriteTransactionStart>,
	IHandle<StorageMessage.WriteTransactionData>,
	IHandle<StorageMessage.WriteTransactionEnd>,
	IHandle<StorageMessage.WriteCommit>,
	IHandle<MonitoringMessage.InternalStatsRequest>,
	IHandle<ReplicationMessage.ReplicaSubscribed>,
	IHandle<ReplicationMessage.CreateChunk>,
	IHandle<ReplicationMessage.RawChunkBulk>,
	IHandle<ReplicationMessage.DataChunkBulk> {

	private bool _paused;
	private readonly object _lock = new();

	public bool Paused {
		get {
			lock (_lock) {
				return _paused;
			}
		}
		private set {
			lock (_lock) {
				_paused = value;
			}
		}
	}

	private readonly ConcurrentQueue<Message> _queue = new();
	public SynchronousScheduler Bus { get; }

	public WriterInterceptor(ISubscriber subscriber) {
		Bus = new("outputBus");
		subscriber.Subscribe<SystemMessage.SystemInit>(this);
		subscriber.Subscribe<SystemMessage.StateChangeMessage>(this);
		subscriber.Subscribe<SystemMessage.WriteEpoch>(this);
		subscriber.Subscribe<SystemMessage.WaitForChaserToCatchUp>(this);
		subscriber.Subscribe<StorageMessage.WritePrepares>(this);
		subscriber.Subscribe<StorageMessage.WriteDelete>(this);
		subscriber.Subscribe<StorageMessage.WriteTransactionStart>(this);
		subscriber.Subscribe<StorageMessage.WriteTransactionData>(this);
		subscriber.Subscribe<StorageMessage.WriteTransactionEnd>(this);
		subscriber.Subscribe<StorageMessage.WriteCommit>(this);
		subscriber.Subscribe<MonitoringMessage.InternalStatsRequest>(this);
		subscriber.Subscribe<ReplicationMessage.ReplicaSubscribed>(this);
		subscriber.Subscribe<ReplicationMessage.CreateChunk>(this);
		subscriber.Subscribe<ReplicationMessage.RawChunkBulk>(this);
		subscriber.Subscribe<ReplicationMessage.DataChunkBulk>(this);
	}

	public void Handle(SystemMessage.SystemInit message) => Process(message);
	public void Handle(SystemMessage.StateChangeMessage message) => Process(message);
	public void Handle(SystemMessage.WriteEpoch message) => Process(message);
	public void Handle(SystemMessage.WaitForChaserToCatchUp message) => Process(message);
	public void Handle(StorageMessage.WritePrepares message) => Process(message);
	public void Handle(StorageMessage.WriteDelete message) => Process(message);
	public void Handle(StorageMessage.WriteTransactionStart message) => Process(message);
	public void Handle(StorageMessage.WriteTransactionData message) => Process(message);
	public void Handle(StorageMessage.WriteTransactionEnd message) => Process(message);
	public void Handle(StorageMessage.WriteCommit message) => Process(message);
	public void Handle(MonitoringMessage.InternalStatsRequest message) => Process(message);
	public void Handle(ReplicationMessage.ReplicaSubscribed message) => Process(message);
	public void Handle(ReplicationMessage.CreateChunk message) => Process(message);
	public void Handle(ReplicationMessage.RawChunkBulk message)  => Process(message);
	public void Handle(ReplicationMessage.DataChunkBulk message) => Process(message);

	protected virtual void Process(Message message) {
		lock (_lock) {
			if (!_paused)
				Bus.Publish(message);
			else
				_queue.Enqueue(message);
		}
	}

	public virtual void Pause() {
		lock (_lock) {
			_paused = true;
		}
	}

	public virtual void Resume() {
		lock (_lock) {
			_paused = false;

			var msgs = new List<Message>();
			while (_queue.TryDequeue(out var msg))
				msgs.Add(msg);

			foreach (var msg in msgs)
				Process(msg);
		}
	}

	public virtual void Reset() {
		lock (_lock) {
			_paused = false;
			_queue.Clear();
		}
	}
}
