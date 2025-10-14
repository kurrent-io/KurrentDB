// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Services.RequestManager;
using KurrentDB.Core.Services.RequestManager.Managers;
using KurrentDB.Core.Tests.Fakes;
using KurrentDB.Core.Tests.Services.Replication;
using NUnit.Framework;

namespace KurrentDB.Core.Tests.Services.RequestManagement;

public abstract class RequestManagerSpecification<TManager>
	where TManager : RequestManagerBase {
	protected readonly TimeSpan PrepareTimeout = TimeSpan.FromMinutes(5);
	protected readonly TimeSpan CommitTimeout = TimeSpan.FromMinutes(5);

	protected TManager Manager;
	protected List<Message> Produced;
	protected FakePublisher Publisher = new();
	protected Guid InternalCorrId = Guid.NewGuid();
	protected Guid ClientCorrId = Guid.NewGuid();
	protected byte[] Metadata = new byte[255];
	protected byte[] EventData = new byte[255];
	protected FakeEnvelope Envelope = new();
	protected CommitSource CommitSource = new();
	protected SynchronousScheduler Dispatcher = new(nameof(RequestManagerSpecification<TManager>));

	protected abstract TManager OnManager(FakePublisher publisher);
	protected abstract IEnumerable<Message> WithInitialMessages();
	protected virtual void Given() { }
	protected abstract Message When();

	protected Event DummyEvent() {
		return new Event(Guid.NewGuid(), "test", false, EventData, false, Metadata);
	}

	protected RequestManagerSpecification() {
		Dispatcher.Subscribe<ReplicationTrackingMessage.ReplicatedTo>(CommitSource);
	}
	[OneTimeSetUp]
	public virtual void Setup() {
		Envelope.Replies.Clear();
		Publisher.Messages.Clear();

		Manager = OnManager(Publisher);
		Dispatcher.Subscribe<StorageMessage.UncommittedPrepareChased>(Manager);
		Dispatcher.Subscribe<StorageMessage.InvalidTransaction>(Manager);
		Dispatcher.Subscribe<StorageMessage.StreamDeleted>(Manager);
		Dispatcher.Subscribe<StorageMessage.WrongExpectedVersion>(Manager);
		Dispatcher.Subscribe<StorageMessage.AlreadyCommitted>(Manager);
		Dispatcher.Subscribe<StorageMessage.RequestManagerTimerTick>(Manager);
		Dispatcher.Subscribe<StorageMessage.CommitIndexed>(Manager);
		Dispatcher.Subscribe<ReplicationTrackingMessage.IndexedTo>(CommitSource);
		Dispatcher.Subscribe<ReplicationTrackingMessage.ReplicatedTo>(CommitSource);

		Manager.Start();
		Given();
		foreach (var msg in WithInitialMessages()) {
			Dispatcher.Publish(msg);
		}

		Publisher.Messages.Clear();
		Envelope.Replies.Clear();
		Dispatcher.Publish(When());
		Produced = new List<Message>(Publisher.Messages);
	}

}
