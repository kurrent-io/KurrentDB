// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Bus;
using EventStore.Core.Helpers;
using EventStore.Projections.Core.Messages;

namespace EventStore.Projections.Core.Services.Processing.Subscriptions;

public interface IReaderSubscription : IHandle<ReaderSubscriptionMessage.CommittedEventDistributed>,
	IHandle<ReaderSubscriptionMessage.EventReaderIdle>,
	IHandle<ReaderSubscriptionMessage.EventReaderStarting>,
	IHandle<ReaderSubscriptionMessage.EventReaderEof>,
	IHandle<ReaderSubscriptionMessage.EventReaderPartitionEof>,
	IHandle<ReaderSubscriptionMessage.EventReaderPartitionDeleted>,
	IHandle<ReaderSubscriptionMessage.EventReaderNotAuthorized>,
	IHandle<ReaderSubscriptionMessage.ReportProgress> {
	string Tag { get; }
	Guid SubscriptionId { get; }
	IEventReader CreatePausedEventReader(IPublisher publisher, IODispatcher ioDispatcher, Guid forkedEventReaderId);
}
