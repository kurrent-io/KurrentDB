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

namespace EventStore.Projections.Core.EventReaders.Feeds {
	public class FeedReader : IHandle<EventReaderSubscriptionMessage.CommittedEventReceived>,
		IHandle<EventReaderSubscriptionMessage.EofReached>,
		IHandle<EventReaderSubscriptionMessage.PartitionEofReached>,
		IHandle<EventReaderSubscriptionMessage.CheckpointSuggested>,
		IHandle<EventReaderSubscriptionMessage.NotAuthorized> {
		private readonly
			PublishSubscribeDispatcher
			<Guid, ReaderSubscriptionManagement.Subscribe,
				ReaderSubscriptionManagement.ReaderSubscriptionManagementMessage, EventReaderSubscriptionMessageBase>
			_subscriptionDispatcher;

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
			PublishSubscribeDispatcher
				<Guid, ReaderSubscriptionManagement.Subscribe,
					ReaderSubscriptionManagement.ReaderSubscriptionManagementMessage, EventReaderSubscriptionMessageBase
				>
				publishSubscribeDispatcher, FeedReaderMessage.ReadPage message, ITimeProvider timeProvider) {
			return new FeedReader(
				publishSubscribeDispatcher, message.User, message.QuerySource, message.FromPosition, message.MaxEvents,
				message.CorrelationId, message.Envelope, timeProvider);
		}

		public FeedReader(
			PublishSubscribeDispatcher
				<Guid, ReaderSubscriptionManagement.Subscribe,
					ReaderSubscriptionManagement.ReaderSubscriptionManagementMessage, EventReaderSubscriptionMessageBase
				>
				subscriptionDispatcher, ClaimsPrincipal user, QuerySourcesDefinition querySource, CheckpointTag fromPosition,
			int maxEvents, Guid requestCorrelationId, IEnvelope replyEnvelope, ITimeProvider timeProvider) {
			if (subscriptionDispatcher == null)
				throw new ArgumentNullException("subscriptionDispatcher");
			if (querySource == null)
				throw new ArgumentNullException("querySource");
			if (fromPosition == null)
				throw new ArgumentNullException("fromPosition");
			if (replyEnvelope == null)
				throw new ArgumentNullException("replyEnvelope");
			if (maxEvents <= 0)
				throw new ArgumentException("non-negative expected", "maxEvents");

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

			_subscriptionId =
				_subscriptionDispatcher.PublishSubscribe(
					new ReaderSubscriptionManagement.Subscribe(
						Guid.NewGuid(), _fromPosition, readerStrategy, readerOptions), this);
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
}
