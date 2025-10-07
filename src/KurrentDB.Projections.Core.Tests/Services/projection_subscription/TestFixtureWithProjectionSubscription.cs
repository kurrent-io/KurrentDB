// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Services.TimerService;
using KurrentDB.Core.Tests.Bus.Helpers;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services;
using KurrentDB.Projections.Core.Services.Processing;
using KurrentDB.Projections.Core.Services.Processing.Strategies;
using KurrentDB.Projections.Core.Services.Processing.Subscriptions;
using NUnit.Framework;

namespace KurrentDB.Projections.Core.Tests.Services.projection_subscription;

public abstract class TestFixtureWithProjectionSubscription {
	protected Guid _projectionCorrelationId;
	protected TestHandler<EventReaderSubscriptionMessage.CommittedEventReceived> _eventHandler;
	protected TestHandler<EventReaderSubscriptionMessage.CheckpointSuggested> _checkpointHandler;
	protected TestHandler<EventReaderSubscriptionMessage.ProgressChanged> _progressHandler;
	protected TestHandler<EventReaderSubscriptionMessage.SubscriptionStarted> _subscriptionStartedHandler;
	protected TestHandler<EventReaderSubscriptionMessage.NotAuthorized> _notAuthorizedHandler;
	protected TestHandler<EventReaderSubscriptionMessage.EofReached> _eofHandler;
	protected TestHandler<EventReaderSubscriptionMessage.PartitionDeleted> _partitionDeletedHandler;
	protected IReaderSubscription _subscription;
	protected ITimeProvider _timeProvider;
	protected SynchronousScheduler _bus;
	protected Action<SourceDefinitionBuilder> _source = null;
	protected int _checkpointUnhandledBytesThreshold;
	protected int _checkpointProcessedEventsThreshold;
	protected int _checkpointAfterMs;
	protected IReaderStrategy _readerStrategy;

	[SetUp]
	public void setup() {
		_checkpointUnhandledBytesThreshold = 1000;
		_checkpointProcessedEventsThreshold = 2000;
		_checkpointAfterMs = 10000;
		_timeProvider = new RealTimeProvider();
		Given();
		_bus = new();
		_projectionCorrelationId = Guid.NewGuid();
		_eventHandler = new TestHandler<EventReaderSubscriptionMessage.CommittedEventReceived>();
		_checkpointHandler = new TestHandler<EventReaderSubscriptionMessage.CheckpointSuggested>();
		_progressHandler = new TestHandler<EventReaderSubscriptionMessage.ProgressChanged>();
		_subscriptionStartedHandler = new TestHandler<EventReaderSubscriptionMessage.SubscriptionStarted>();
		_notAuthorizedHandler = new TestHandler<EventReaderSubscriptionMessage.NotAuthorized>();
		_eofHandler = new TestHandler<EventReaderSubscriptionMessage.EofReached>();
		_partitionDeletedHandler = new TestHandler<EventReaderSubscriptionMessage.PartitionDeleted>();

		_bus.Subscribe(_eventHandler);
		_bus.Subscribe(_checkpointHandler);
		_bus.Subscribe(_progressHandler);
		_bus.Subscribe(_eofHandler);
		_readerStrategy = CreateCheckpointStrategy();
		_subscription = CreateProjectionSubscription();


		When();
	}

	protected virtual IReaderSubscription CreateProjectionSubscription() {
		return new ReaderSubscription(_bus,
			_projectionCorrelationId,
			_readerStrategy.PositionTagger.MakeZeroCheckpointTag(),
			_readerStrategy,
			_timeProvider,
			_checkpointUnhandledBytesThreshold,
			_checkpointProcessedEventsThreshold,
			_checkpointAfterMs,
			false,
			null,
			false);
	}

	protected virtual void Given() {
	}

	protected abstract void When();

	protected virtual IReaderStrategy CreateCheckpointStrategy() {
		var readerBuilder = new SourceDefinitionBuilder();
		if (_source != null) {
			_source(readerBuilder);
		} else {
			readerBuilder.FromAll();
			readerBuilder.AllEvents();
		}

		var config = new ProjectionConfig(null, 1000, 1000 * 1000, 100, 500, true, true, false, false, true, 10000,
			1, 250);
		IQuerySources sources = readerBuilder.Build();
		var readerStrategy = ReaderStrategy.Create(
			"test",
			0,
			sources,
			_timeProvider,
			runAs: config.RunAs);
		return readerStrategy;
	}
}
