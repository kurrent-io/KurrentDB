// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Helpers;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Services.TimerService;
using KurrentDB.Core.Services.UserManagement;
using KurrentDB.Core.TransactionLog.Checkpoint;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services.Processing.Subscriptions;
using KurrentDB.Projections.Core.Services.Processing.TransactionFile;
using Serilog;
using HeadingEventReader = KurrentDB.Projections.Core.Services.Processing.TransactionFile.HeadingEventReader;
using ILogger = Serilog.ILogger;

namespace KurrentDB.Projections.Core.Services.Processing;

public class EventReaderCoreService
	: IHandle<ReaderCoreServiceMessage.StartReader>,
		IHandle<ReaderCoreServiceMessage.StopReader>,
		IHandle<ReaderSubscriptionManagement.Subscribe>,
		IHandle<ReaderSubscriptionManagement.Unsubscribe>,
		IHandle<ReaderSubscriptionManagement.Pause>,
		IHandle<ReaderSubscriptionManagement.Resume>,
		IHandle<ReaderSubscriptionMessage.CommittedEventDistributed>,
		IHandle<ReaderSubscriptionMessage.EventReaderIdle>,
		IHandle<ReaderSubscriptionMessage.EventReaderStarting>,
		IHandle<ReaderSubscriptionMessage.EventReaderNotAuthorized>,
		IHandle<ReaderSubscriptionMessage.EventReaderEof>,
		IHandle<ReaderSubscriptionMessage.EventReaderPartitionDeleted>,
		IHandle<ReaderSubscriptionMessage.Faulted>,
		IHandle<ReaderSubscriptionMessage.ReportProgress> {
	public const string SubComponentName = "EventReaderCoreService";

	private readonly IPublisher _publisher;
	private readonly IODispatcher _ioDispatcher;
	private readonly ILogger _logger = Log.ForContext<ProjectionCoreService>();
	private bool _stopped = true;

	private readonly Dictionary<Guid, IReaderSubscription> _subscriptions = new();
	private readonly Dictionary<Guid, IEventReader> _eventReaders = new();
	private readonly Dictionary<Guid, Guid> _subscriptionEventReaders = new();
	private readonly Dictionary<Guid, Guid> _eventReaderSubscriptions = new();
	private readonly HashSet<Guid> _pausedSubscriptions = [];
	private readonly HeadingEventReader _headingEventReader;
	private readonly ICheckpoint _writerCheckpoint;
	private readonly bool _runHeadingReader;
	private readonly bool _faultOutOfOrderProjections;
	private readonly IEnvelope _publishEnvelope;
	private Guid _defaultEventReaderId;
	private Guid _reportProgressId;

	public EventReaderCoreService(
		IPublisher publisher,
		IODispatcher ioDispatcher,
		int eventCacheSize,
		ICheckpoint writerCheckpoint,
		bool runHeadingReader,
		bool faultOutOfOrderProjections) {
		_publisher = publisher;
		_ioDispatcher = ioDispatcher;
		if (runHeadingReader)
			_headingEventReader = new(eventCacheSize, _publisher);
		_writerCheckpoint = writerCheckpoint;
		_runHeadingReader = runHeadingReader;
		_faultOutOfOrderProjections = faultOutOfOrderProjections;
		_publishEnvelope = publisher;
	}

	public void Handle(ReaderSubscriptionManagement.Pause message) {
		if (!_pausedSubscriptions.Add(message.SubscriptionId))
			throw new InvalidOperationException("Already paused projection");

		if (!_subscriptions.TryGetValue(message.SubscriptionId, out var projectionSubscription))
			return; // may be already unsubscribed when self-unsubscribing

		var eventReaderId = _subscriptionEventReaders[message.SubscriptionId];
		if (eventReaderId == Guid.Empty) // head
		{
			_subscriptionEventReaders.Remove(message.SubscriptionId);
			_headingEventReader.Unsubscribe(message.SubscriptionId);
			var forkedEventReaderId = Guid.NewGuid();
			var forkedEventReader = projectionSubscription.CreatePausedEventReader(
				_publisher, _ioDispatcher, forkedEventReaderId);
			_subscriptionEventReaders.Add(message.SubscriptionId, forkedEventReaderId);
			_eventReaderSubscriptions.Add(forkedEventReaderId, message.SubscriptionId);
			_eventReaders.Add(forkedEventReaderId, forkedEventReader);
			_publisher.Publish(new EventReaderSubscriptionMessage.ReaderAssignedReader(message.SubscriptionId, forkedEventReaderId));
		} else {
			_eventReaders[eventReaderId].Pause();
		}
	}

	public void Handle(ReaderSubscriptionManagement.Resume message) {
		if (!_pausedSubscriptions.Remove(message.SubscriptionId))
			throw new InvalidOperationException("Not a paused projection");
		var eventReader = _subscriptionEventReaders[message.SubscriptionId];
		_eventReaders[eventReader].Resume();
	}

	public void Handle(ReaderSubscriptionManagement.Subscribe message) {
		if (_stopped) {
			_publisher.Publish(new EventReaderSubscriptionMessage.Failed(message.SubscriptionId, $"{nameof(EventReaderCoreService)} is stopped"));
			return;
		}

		var fromCheckpointTag = message.FromPosition;
		var subscriptionId = message.SubscriptionId;
		try {
			var projectionSubscription = message.ReaderStrategy.CreateReaderSubscription(
				_publisher, fromCheckpointTag, message.SubscriptionId, message.Options);
			_subscriptions.Add(subscriptionId, projectionSubscription);

			var distributionPointCorrelationId = Guid.NewGuid();
			var eventReader = projectionSubscription.CreatePausedEventReader(_publisher, _ioDispatcher, distributionPointCorrelationId);
			_eventReaders.Add(distributionPointCorrelationId, eventReader);
			_subscriptionEventReaders.Add(subscriptionId, distributionPointCorrelationId);
			_eventReaderSubscriptions.Add(distributionPointCorrelationId, subscriptionId);
			_publisher.Publish(new EventReaderSubscriptionMessage.ReaderAssignedReader(subscriptionId, distributionPointCorrelationId));
			eventReader.Resume();
		} catch (Exception ex) {
			_publisher.Publish(new EventReaderSubscriptionMessage.Failed(subscriptionId, ex.ToString()));
		}
	}

	public void Handle(ReaderSubscriptionManagement.Unsubscribe message) {
		if (!_pausedSubscriptions.Contains(message.SubscriptionId))
			Handle(new ReaderSubscriptionManagement.Pause(message.SubscriptionId));
		_subscriptionEventReaders.TryGetValue(message.SubscriptionId, out var eventReaderId);
		if (eventReaderId != Guid.Empty) {
			_eventReaders[eventReaderId].Dispose();
			_eventReaders.Remove(eventReaderId);
			_eventReaderSubscriptions.Remove(eventReaderId);
			_publisher.Publish(new EventReaderSubscriptionMessage.ReaderAssignedReader(message.SubscriptionId, Guid.Empty));
		}

		_pausedSubscriptions.Remove(message.SubscriptionId);
		_subscriptionEventReaders.Remove(message.SubscriptionId);
		_subscriptions.Remove(message.SubscriptionId);
	}

	public void Handle(ReaderSubscriptionMessage.CommittedEventDistributed message) {
		if (_stopped)
			return;
		if (_runHeadingReader && _headingEventReader.Handle(message))
			return;
		if (!_eventReaderSubscriptions.TryGetValue(message.CorrelationId, out var projectionId))
			return; // unsubscribed

		if (TrySubscribeHeadingEventReader(message, projectionId))
			return;
		if (message.Data != default) {
			try {
				_subscriptions[projectionId].Handle(message);
			} catch (Exception ex) {
				var subscription = _subscriptions[projectionId];
				Handle(new ReaderSubscriptionManagement.Unsubscribe(subscription.SubscriptionId));
				_publisher.Publish(new EventReaderSubscriptionMessage.Failed(subscription.SubscriptionId,
					$"The subscription failed to handle an event {message.Data.EventStreamId}:{message.Data.EventType}@{message.Data.EventSequenceNumber} because {ex.Message}"));
			}
		}
	}

	public void Handle(ReaderSubscriptionMessage.EventReaderIdle message) {
		if (_stopped)
			return;
		if (_runHeadingReader && _headingEventReader.Handle(message))
			return;
		if (!_eventReaderSubscriptions.TryGetValue(message.CorrelationId, out var projectionId))
			return; // unsubscribed
		_subscriptions[projectionId].Handle(message);
	}

	public void Handle(ReaderSubscriptionMessage.EventReaderStarting message) {
		if (_stopped)
			return;
		if (!_eventReaderSubscriptions.TryGetValue(message.CorrelationId, out var projectionId))
			return; // unsubscribed
		_subscriptions[projectionId].Handle(message);
	}

	public void Handle(ReaderSubscriptionMessage.EventReaderEof message) {
		if (_stopped)
			return;
		if (!_eventReaderSubscriptions.TryGetValue(message.CorrelationId, out var projectionId))
			return; // unsubscribed
		_subscriptions[projectionId].Handle(message);
	}

	public void Handle(ReaderSubscriptionMessage.EventReaderPartitionDeleted message) {
		if (_stopped)
			return;
		if (_runHeadingReader && _headingEventReader.Handle(message))
			return;
		if (!_eventReaderSubscriptions.TryGetValue(message.CorrelationId, out var projectionId))
			return; // unsubscribed
		_subscriptions[projectionId].Handle(message);
	}

	public void Handle(ReaderSubscriptionMessage.EventReaderNotAuthorized message) {
		if (_stopped)
			return;
		if (!_eventReaderSubscriptions.TryGetValue(message.CorrelationId, out var projectionId))
			return; // unsubscribed
		_subscriptions[projectionId].Handle(message);

		_pausedSubscriptions.Add(projectionId); // it is actually disposed -- workaround
		Handle(new ReaderSubscriptionManagement.Unsubscribe(projectionId));
	}

	public void Handle(ReaderSubscriptionMessage.Faulted message) {
		if (_stopped)
			return;
		if (!_eventReaderSubscriptions.TryGetValue(message.CorrelationId, out var projectionId))
			return; // unsubscribed

		if (!_faultOutOfOrderProjections && message.Reason.Contains("was expected in the stream")) {
			// Log without fault the projection
			_logger.Verbose(message.Reason);
			return;
		}

		var subscription = _subscriptions[projectionId];
		Handle(new ReaderSubscriptionManagement.Unsubscribe(subscription.SubscriptionId));
		_publisher.Publish(new EventReaderSubscriptionMessage.Failed(subscription.SubscriptionId, message.Reason));
	}

	public void Handle(ReaderSubscriptionMessage.ReportProgress message) {
		if (_stopped || message.CorrelationId != _reportProgressId)
			return;

		foreach (var subscription in _subscriptions.Values) {
			subscription.Handle(message);
		}

		_reportProgressId = Guid.NewGuid();
		_publisher.Publish(TimerMessage.Schedule.Create(TimeSpan.FromMilliseconds(500), _publishEnvelope,
			new ReaderSubscriptionMessage.ReportProgress(_reportProgressId)));
	}

	private void StartReaders() {
		//TODO: do we need to clear subscribed projections here?
		//TODO: do we need to clear subscribed distribution points here?
		_stopped = false;
		_defaultEventReaderId = Guid.NewGuid();
		var transactionFileReader = new TransactionFileEventReader(
			_publisher,
			_defaultEventReaderId,
			SystemAccounts.System,
			new TFPos(_writerCheckpoint.Read(), -1),
			new RealTimeProvider(),
			deliverEndOfTFPosition: false);

		_eventReaders.Add(_defaultEventReaderId, transactionFileReader);
		if (_runHeadingReader)
			_headingEventReader.Start(_defaultEventReaderId, transactionFileReader);
	}

	private void StopReaders(ReaderCoreServiceMessage.StopReader message) {
		if (_eventReaders.TryGetValue(_defaultEventReaderId, out var eventReader)) {
			eventReader.Dispose();
			_eventReaders.Remove(_defaultEventReaderId);
			_eventReaderSubscriptions.Remove(_defaultEventReaderId);
		}

		_defaultEventReaderId = Guid.Empty;

		if (_subscriptions.Count > 0) {
			_logger.Information("_subscriptions is not empty after all the projections have been killed");
			_subscriptions.Clear();
		}

		if (_eventReaders.Count > 0) {
			_logger.Information("_eventReaders is not empty after all the projections have been killed");
			_eventReaders.Clear();
		}

		if (_subscriptionEventReaders.Count > 0) {
			_logger.Information("_subscriptionEventReaders is not empty after all the projections have been killed");
			_subscriptionEventReaders.Clear();
		}

		if (_eventReaderSubscriptions.Count > 0) {
			_logger.Information("_eventReaderSubscriptions is not empty after all the projections have been killed");
			_eventReaderSubscriptions.Clear();
		}

		if (_runHeadingReader)
			_headingEventReader.Stop();
		_stopped = true;

		_publisher.Publish(new ProjectionCoreServiceMessage.SubComponentStopped(SubComponentName, message.QueueId));
	}

	private bool TrySubscribeHeadingEventReader(ReaderSubscriptionMessage.CommittedEventDistributed message, Guid projectionId) {
		if (message.SafeTransactionFileReaderJoinPosition == null)
			return false;

		if (!_runHeadingReader)
			return false;

		if (_pausedSubscriptions.Contains(projectionId))
			return false;

		var projectionSubscription = _subscriptions[projectionId];

		if (
			!_headingEventReader.TrySubscribe(projectionId, projectionSubscription, message.SafeTransactionFileReaderJoinPosition.Value))
			return false;

		Guid eventReaderId = message.CorrelationId;
		_eventReaders[eventReaderId].Dispose();
		_eventReaders.Remove(eventReaderId);
		_eventReaderSubscriptions.Remove(eventReaderId);
		_subscriptionEventReaders[projectionId] = Guid.Empty;
		_publisher.Publish(new EventReaderSubscriptionMessage.ReaderAssignedReader(message.CorrelationId, Guid.Empty));
		return true;
	}

	public void Handle(ReaderCoreServiceMessage.StartReader message) {
		StartReaders();
		_publisher.Publish(new ProjectionCoreServiceMessage.SubComponentStarted(SubComponentName, message.InstanceCorrelationId));
		_reportProgressId = Guid.NewGuid();
		_publisher.Publish(TimerMessage.Schedule.Create(TimeSpan.FromMilliseconds(500), _publishEnvelope,
			new ReaderSubscriptionMessage.ReportProgress(_reportProgressId)));
	}

	public void Handle(ReaderCoreServiceMessage.StopReader message) {
		StopReaders(message);
	}
}
