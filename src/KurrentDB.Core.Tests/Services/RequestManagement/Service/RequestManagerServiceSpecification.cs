// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using EventStore.ClientAPI.Common.Utils;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Services;
using KurrentDB.Core.Services.RequestManager;
using KurrentDB.Core.Tests.Fakes;
using KurrentDB.Core.Tests.Services.Replication;
using KurrentDB.Core.TransactionLog.LogRecords;
using NUnit.Framework;

namespace KurrentDB.Core.Tests.Services.RequestManagement.Service;

public abstract class RequestManagerServiceSpecification :
	IHandle<StorageMessage.WritePrepares>,
	IHandle<StorageMessage.RequestCompleted> {
	protected readonly TimeSpan PrepareTimeout = TimeSpan.FromMinutes(5);
	protected readonly TimeSpan CommitTimeout = TimeSpan.FromMinutes(5);

	protected List<Message> Produced = new();
	protected FakePublisher Publisher = new();
	protected Guid InternalCorrId = Guid.NewGuid();
	protected Guid ClientCorrId = Guid.NewGuid();
	protected byte[] Metadata = Helper.UTF8NoBom.GetBytes("{Value:42}");
	protected byte[] EventData = Helper.UTF8NoBom.GetBytes("{Value:43}");
	protected FakeEnvelope Envelope = new();
	protected SynchronousScheduler Dispatcher = new(nameof(RequestManagerServiceSpecification));
	protected RequestManagementService Service;
	protected bool GrantAccess = true;
	protected long LogPosition = 100;
	protected PrepareFlags PrepareFlags = PrepareFlags.Data;
	protected string StreamId = $"{nameof(RequestManagerServiceSpecification)}-{Guid.NewGuid()}";

	protected abstract void Given();
	protected abstract Message When();


	protected RequestManagerServiceSpecification() {
		Dispatcher.Subscribe<StorageMessage.WritePrepares>(this);
		Dispatcher.Subscribe<StorageMessage.RequestCompleted>(this);

		Service = new RequestManagementService(
			Dispatcher,
			TimeSpan.FromSeconds(2),
			TimeSpan.FromSeconds(2),
			explicitTransactionsSupported: true);
		Dispatcher.Subscribe<ClientMessage.WriteEvents>(Service);
		Dispatcher.Subscribe<StorageMessage.UncommittedPrepareChased>(Service);
		Dispatcher.Subscribe<StorageMessage.InvalidTransaction>(Service);
		Dispatcher.Subscribe<StorageMessage.StreamDeleted>(Service);
		Dispatcher.Subscribe<StorageMessage.WrongExpectedVersion>(Service);
		Dispatcher.Subscribe<StorageMessage.AlreadyCommitted>(Service);
		Dispatcher.Subscribe<StorageMessage.RequestManagerTimerTick>(Service);
		Dispatcher.Subscribe<StorageMessage.CommitIndexed>(Service);
		Dispatcher.Subscribe<ReplicationTrackingMessage.IndexedTo>(Service);
		Dispatcher.Subscribe<ReplicationTrackingMessage.ReplicatedTo>(Service);
		Dispatcher.Subscribe<SystemMessage.StateChangeMessage>(Service);
	}
	[OneTimeSetUp]
	public virtual void Setup() {
		Envelope.Replies.Clear();
		Publisher.Messages.Clear();

		Given();
		Envelope.Replies.Clear();
		Publisher.Messages.Clear();

		Dispatcher.Publish(When());

	}


	private static readonly StreamAcl PublicStream = new StreamAcl(SystemRoles.All, SystemRoles.All, SystemRoles.All,
		SystemRoles.All, SystemRoles.All);
	public void Handle(StorageMessage.EffectiveStreamAclRequest message) {
		message.Envelope.ReplyWith(new StorageMessage.EffectiveStreamAclResponse(new StorageMessage.EffectiveAcl(
			PublicStream, PublicStream, PublicStream
			)));
	}

	public void Handle(StorageMessage.WritePrepares message) {

		var transactionPosition = LogPosition;
		foreach (var _ in message.Events.Span) {
			Dispatcher.Publish(new StorageMessage.UncommittedPrepareChased(
									message.CorrelationId,
									LogPosition,
									PrepareFlags));
			LogPosition += 100;
		}
		Dispatcher.Publish(StorageMessage.CommitIndexed.ForSingleStream(message.CorrelationId,
			LogPosition,
			transactionPosition,
			0,
			message.Events.Length));
	}

	protected Event DummyEvent() {
		return new Event(Guid.NewGuid(), "SomethingHappened", true, EventData, false, Metadata);
	}

	public void Handle(StorageMessage.RequestCompleted message) {
		Produced.Add(message);
	}
}
