// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Security.Claims;
using EventStore.Common.Utils;
using EventStore.Core.Bus;
using EventStore.Core.Messaging;
using EventStore.Core.Services.TimerService;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Messages.EventReaders.Feeds;
using EventStore.Projections.Core.Services;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using EventStore.Projections.Core.Services.Processing.Strategies;
using EventStore.Projections.Core.Services.Processing.Subscriptions;

namespace EventStore.Projections.Core.EventReaders.Feeds;

public class FeedReader : IHandle<EventReaderSubscriptionMessage.CommittedEventReceived>,
	IHandle<EventReaderSubscriptionMessage.EofReached>,
	IHandle<EventReaderSubscriptionMessage.PartitionEofReached>,
	IHandle<EventReaderSubscriptionMessage.CheckpointSuggested>,
	IHandle<EventReaderSubscriptionMessage.NotAuthorized> {
	private readonly
		ReaderSubscriptionDispatcher _subscriptionDispatcher;

	private readonly ClaimsPrincipal _user;

	private readonly QuerySourcesDefinition _querySource;
	private readonly CheckpointTag _fromPosition;
	private readonly int _maxEvents;
	private readonly Guid _requestCorrelationId;

	private readonly List<TaggedResolvedEvent> _batch = new List<TaggedResolvedEvent>();
	private readonly IEnvelope _replyEnvelope;
	private readonly ITimeProvider _timeProvider;

	private Guid _subscriptionId;
	private CheckpointTag _lastReaderPosition;

	public static FeedReader Create(
		ReaderSubscriptionDispatcher readerSubscriptionDispatcher, FeedReaderMessage.ReadPage message, ITimeProvider timeProvider) {
		return new FeedReader(
			readerSubscriptionDispatcher, message.User, message.QuerySource, message.FromPosition, message.MaxEvents,
			message.CorrelationId, message.Envelope, timeProvider);
	}

	public FeedReader(
		ReaderSubscriptionDispatcher subscriptionDispatcher, ClaimsPrincipal user,
		QuerySourcesDefinition querySource, CheckpointTag fromPosition,
		int maxEvents, Guid requestCorrelationId, IEnvelope replyEnvelope, ITimeProvider timeProvider) {
		ArgumentNullException.ThrowIfNull(subscriptionDispatcher);
		ArgumentNullException.ThrowIfNull(querySource);
		ArgumentNullException.ThrowIfNull(fromPosition);
		ArgumentNullException.ThrowIfNull(replyEnvelope);
		if (maxEvents <= 0) throw new ArgumentException("non-negative expected", nameof(maxEvents));

		_subscriptionDispatcher = subscriptionDispatcher;
		_user = user;
		_querySource = querySource;
		_fromPosition = fromPosition;
		_maxEvents = maxEvents;
		_requestCorrelationId = requestCorrelationId;
		_replyEnvelope = replyEnvelope;
		_timeProvider = timeProvider;
	}

	public void Start() {
		var readerStrategy = ReaderStrategy.Create(
			_querySource.ToJson(), // tag
			0,
			_querySource,
			_timeProvider,
			stopOnEof: true,
			runAs: _user);

		//TODO: make reader mode explicit
		var readerOptions = new ReaderSubscriptionOptions(
			1024 * 1024,
			checkpointAfterMs: 10000,
			checkpointProcessedEventsThreshold: null,
			stopOnEof: true,
			stopAfterNEvents: _maxEvents,
			// The projection must be stopped for debugging, so will enable content type validation automatically
			enableContentTypeValidation: true);

		_subscriptionId = Guid.NewGuid();
		_subscriptionDispatcher.PublishSubscribe(
			new ReaderSubscriptionManagement.Subscribe(
				_subscriptionId, _fromPosition, readerStrategy, readerOptions), this, false);
	}

	public void Handle(EventReaderSubscriptionMessage.CommittedEventReceived message) {
		_lastReaderPosition = message.CheckpointTag;
		_batch.Add(new TaggedResolvedEvent(message.Data, message.CheckpointTag));
	}

	public void Handle(EventReaderSubscriptionMessage.EofReached message) {
		_lastReaderPosition = message.CheckpointTag;
		Reply();
		Unsubscribe();
	}

	public void Handle(EventReaderSubscriptionMessage.PartitionEofReached message) {
		_lastReaderPosition = message.CheckpointTag;
	}

	public void Handle(EventReaderSubscriptionMessage.CheckpointSuggested message) {
		throw new NotSupportedException();
	}

	private void Unsubscribe() {
		_subscriptionDispatcher.Cancel(_subscriptionId);
	}

	private void Reply() {
		_replyEnvelope.ReplyWith(
			new FeedReaderMessage.FeedPage(
				_requestCorrelationId, FeedReaderMessage.FeedPage.ErrorStatus.Success, _batch.ToArray(),
				_lastReaderPosition));
	}

	private void ReplyNotAuthorized() {
		_replyEnvelope.ReplyWith(
			new FeedReaderMessage.FeedPage(
				_requestCorrelationId, FeedReaderMessage.FeedPage.ErrorStatus.NotAuthorized, null,
				_lastReaderPosition));
	}

	public void Handle(EventReaderSubscriptionMessage.NotAuthorized message) {
		ReplyNotAuthorized();
	}
}
