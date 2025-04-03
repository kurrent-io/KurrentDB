// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using EventStore.Core.Bus;
using EventStore.Core.Data;
using EventStore.Core.Helpers;
using EventStore.Core.Services.TimerService;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services.Processing.AllStream;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using EventStore.Projections.Core.Services.Processing.EventByType;
using EventStore.Projections.Core.Services.Processing.MultiStream;
using EventStore.Projections.Core.Services.Processing.SingleStream;
using EventStore.Projections.Core.Services.Processing.Subscriptions;

namespace EventStore.Projections.Core.Services.Processing.Strategies;

public class ReaderStrategy : IReaderStrategy {
	private readonly bool _allStreams;
	private readonly HashSet<string> _categories;
	private readonly HashSet<string> _streams;
	private readonly bool _allEvents;
	private readonly bool _includeLinks;
	private readonly HashSet<string> _events;
	private readonly bool _includeStreamDeletedNotification;
	private readonly bool _reorderEvents;
	private readonly ClaimsPrincipal _runAs;
	private readonly int _processingLag;


	private readonly EventFilter _eventFilter;
	private readonly PositionTagger _positionTagger;
	private readonly ITimeProvider _timeProvider;

	private readonly string _tag;
	private readonly int _phase;

	public static IReaderStrategy Create(
		string tag,
		int phase,
		IQuerySources sources,
		ITimeProvider timeProvider,
		bool stopOnEof,
		ClaimsPrincipal runAs) {
		if (!sources.AllStreams && !sources.HasCategories() && !sources.HasStreams())
			throw new InvalidOperationException("None of streams and categories are included");
		if (!sources.AllEvents && !sources.HasEvents())
			throw new InvalidOperationException("None of events are included");
		if (sources.HasStreams() && sources.HasCategories())
			throw new InvalidOperationException(
				"Streams and categories cannot be included in a filter at the same time");
		if (sources.AllStreams && (sources.HasCategories() || sources.HasStreams()))
			throw new InvalidOperationException("Both FromAll and specific categories/streams cannot be set");
		if (sources.AllEvents && sources.HasEvents())
			throw new InvalidOperationException("Both AllEvents and specific event filters cannot be set");

		if (sources.ByStreams && sources.HasStreams())
			throw new InvalidOperationException(
				"foreachStream projections are not supported on stream based sources");

		if (sources.ReorderEventsOption) {
			if (sources.AllStreams)
				throw new InvalidOperationException("Event reordering cannot be used with fromAll()");
			if (!(sources.HasStreams() && sources.Streams.Length > 1)) {
				throw new InvalidOperationException(
					"Event reordering is only available in fromStreams([]) projections");
			}

			if (sources.ProcessingLagOption < 50)
				throw new InvalidOperationException("Event reordering requires processing lag at least of 50ms");
		}

		if (sources.HandlesDeletedNotifications && !sources.ByStreams)
			throw new InvalidOperationException(
				"Deleted stream notifications are only supported with foreachStream()");

		var readerStrategy = new ReaderStrategy(
			tag,
			phase,
			sources.AllStreams,
			sources.Categories,
			sources.Streams,
			sources.AllEvents,
			sources.IncludeLinksOption,
			sources.Events,
			sources.HandlesDeletedNotifications,
			sources.ProcessingLagOption,
			sources.ReorderEventsOption,
			runAs,
			timeProvider);
		return readerStrategy;
	}

	private ReaderStrategy(
		string tag,
		int phase,
		bool allStreams,
		string[] categories,
		string[] streams,
		bool allEvents,
		bool includeLinks,
		string[] events,
		bool includeStreamDeletedNotification,
		int? processingLag,
		bool reorderEvents,
		ClaimsPrincipal runAs,
		ITimeProvider timeProvider) {
		_tag = tag;
		_phase = phase;
		_allStreams = allStreams;
		_categories = categories != null && categories.Length > 0 ? new HashSet<string>(categories) : null;
		_streams = streams != null && streams.Length > 0 ? new HashSet<string>(streams) : null;
		_allEvents = allEvents;
		_includeLinks = includeLinks;
		_events = events != null && events.Length > 0 ? new HashSet<string>(events) : null;
		_includeStreamDeletedNotification = includeStreamDeletedNotification;
		_processingLag = processingLag.GetValueOrDefault();
		_reorderEvents = reorderEvents;
		_runAs = runAs;

		_eventFilter = CreateEventFilter();
		_positionTagger = CreatePositionTagger();
		_timeProvider = timeProvider;
	}

	public bool IsReadingOrderRepeatable {
		get { return !(_streams != null && _streams.Count > 1); }
	}

	public EventFilter EventFilter {
		get { return _eventFilter; }
	}

	public PositionTagger PositionTagger {
		get { return _positionTagger; }
	}

	public int Phase {
		get { return _phase; }
	}

	public IReaderSubscription CreateReaderSubscription(
		IPublisher publisher, CheckpointTag fromCheckpointTag, Guid subscriptionId,
		ReaderSubscriptionOptions readerSubscriptionOptions) {
		if (_reorderEvents)
			return new EventReorderingReaderSubscription(
				publisher,
				subscriptionId,
				fromCheckpointTag,
				this,
				_timeProvider,
				readerSubscriptionOptions.CheckpointUnhandledBytesThreshold,
				readerSubscriptionOptions.CheckpointProcessedEventsThreshold,
				readerSubscriptionOptions.CheckpointAfterMs,
				_processingLag,
				readerSubscriptionOptions.StopOnEof,
				readerSubscriptionOptions.StopAfterNEvents,
				readerSubscriptionOptions.EnableContentTypeValidation);
		else
			return new ReaderSubscription(
				_tag,
				publisher,
				subscriptionId,
				fromCheckpointTag,
				this,
				_timeProvider,
				readerSubscriptionOptions.CheckpointUnhandledBytesThreshold,
				readerSubscriptionOptions.CheckpointProcessedEventsThreshold,
				readerSubscriptionOptions.CheckpointAfterMs,
				readerSubscriptionOptions.StopOnEof,
				readerSubscriptionOptions.StopAfterNEvents,
				readerSubscriptionOptions.EnableContentTypeValidation);
	}

	public IEventReader CreatePausedEventReader(
		Guid eventReaderId, IPublisher publisher, IODispatcher ioDispatcher, CheckpointTag checkpointTag,
		bool stopOnEof, int? stopAfterNEvents) {
		if (_allStreams && _events != null && _events.Count >= 1) {
			//IEnumerable<string> streams = GetEventIndexStreams();
			return CreatePausedEventIndexEventReader(
				eventReaderId, ioDispatcher, publisher, checkpointTag, stopOnEof, stopAfterNEvents, true, _events,
				_includeStreamDeletedNotification);
		}

		if (_allStreams) {
			var eventReader = new TransactionFileEventReader(publisher, eventReaderId, _runAs,
				new TFPos(checkpointTag.CommitPosition.Value, checkpointTag.PreparePosition.Value), _timeProvider,
				deliverEndOfTFPosition: true, stopOnEof: stopOnEof, resolveLinkTos: false);
			return eventReader;
		}

		if (_streams != null && _streams.Count == 1) {
			var streamName = checkpointTag.Streams.Keys.First();
			//TODO: handle if not the same
			return CreatePausedStreamEventReader(
				eventReaderId, ioDispatcher, publisher, checkpointTag, streamName, stopOnEof, resolveLinkTos: true,
				stopAfterNEvents: stopAfterNEvents, produceStreamDeletes: _includeStreamDeletedNotification);
		}

		if (_categories != null && _categories.Count == 1) {
			var streamName = checkpointTag.Streams.Keys.First();
			return CreatePausedStreamEventReader(
				eventReaderId, ioDispatcher, publisher, checkpointTag, streamName, stopOnEof, resolveLinkTos: true,
				stopAfterNEvents: stopAfterNEvents, produceStreamDeletes: _includeStreamDeletedNotification);
		}

		if (_streams != null && _streams.Count > 1) {
			return CreatePausedMultiStreamEventReader(
				eventReaderId, ioDispatcher, publisher, checkpointTag, stopOnEof, stopAfterNEvents, true, _streams);
		}

		throw new NotSupportedException();
	}

	//TODO: clean up $deleted event notification vs $streamDeleted event

	private EventFilter CreateEventFilter() {
		if (_allStreams && _events != null && _events.Count >= 1)
			return new EventByTypeIndexEventFilter(_events);
		if (_allStreams)
			//NOTE: a projection cannot handle both stream deleted notifications
			// and real stream tombstone/stream deleted events as they have the same position
			// and thus processing cannot be correctly checkpointed
			return new TransactionFileEventFilter(
				_allEvents, !_includeStreamDeletedNotification, _events, includeLinks: _includeLinks);
		if (_categories != null && _categories.Count == 1)
			return new CategoryEventFilter(_categories.First(), _allEvents, _events);
		if (_categories != null)
			throw new NotSupportedException();
		if (_streams != null && _streams.Count == 1)
			return new StreamEventFilter(_streams.First(), _allEvents, _events);
		if (_streams != null && _streams.Count > 1)
			return new MultiStreamEventFilter(_streams, _allEvents, _events);
		throw new NotSupportedException();
	}

	private PositionTagger CreatePositionTagger() {
		if (_allStreams && _events != null && _events.Count >= 1)
			return new EventByTypeIndexPositionTagger(_phase, _events.ToArray(), _includeStreamDeletedNotification);
		if (_allStreams && _reorderEvents)
			return new PreparePositionTagger(_phase);
		if (_allStreams)
			return new TransactionFilePositionTagger(_phase);
		if (_categories != null && _categories.Count == 1)
			//TODO: '-' is a hardcoded separator
			return new StreamPositionTagger(_phase, "$ce-" + _categories.First());
		if (_categories != null)
			throw new NotSupportedException();
		if (_streams != null && _streams.Count == 1)
			return new StreamPositionTagger(_phase, _streams.First());
		if (_streams != null && _streams.Count > 1)
			return new MultiStreamPositionTagger(_phase, _streams.ToArray());
		//TODO: consider passing projection phase from outside (above)
		throw new NotSupportedException();
	}


	private IEventReader CreatePausedStreamEventReader(
		Guid eventReaderId, IODispatcher ioDispatcher, IPublisher publisher, CheckpointTag checkpointTag,
		string streamName, bool stopOnEof, int? stopAfterNEvents, bool resolveLinkTos, bool produceStreamDeletes) {
		var lastProcessedSequenceNumber = checkpointTag.Streams.Values.First();
		var fromSequenceNumber = lastProcessedSequenceNumber + 1;
		var eventReader = new StreamEventReader(publisher, eventReaderId, _runAs, streamName, fromSequenceNumber,
			_timeProvider,
			resolveLinkTos, produceStreamDeletes, stopOnEof);
		return eventReader;
	}

	private IEventReader CreatePausedEventIndexEventReader(
		Guid eventReaderId, IODispatcher ioDispatcher, IPublisher publisher, CheckpointTag checkpointTag,
		bool stopOnEof, int? stopAfterNEvents, bool resolveLinkTos, IEnumerable<string> eventTypes,
		bool includeStreamDeletedNotification) {
		//NOTE: just optimization - anyway if reading from TF events may reappear
		long p;
		var nextPositions = eventTypes.ToDictionary(
			v => "$et-" + v, v => checkpointTag.Streams.TryGetValue(v, out p) ? p + 1 : 0);

		if (includeStreamDeletedNotification)
			nextPositions.Add("$et-$deleted", checkpointTag.Streams.TryGetValue("$deleted", out p) ? p + 1 : 0);

		return new EventByTypeIndexEventReader(publisher, eventReaderId, _runAs, eventTypes.ToArray(),
			includeStreamDeletedNotification,
			checkpointTag.Position, nextPositions, resolveLinkTos, _timeProvider, stopOnEof);
	}

	private IEventReader CreatePausedMultiStreamEventReader(
		Guid eventReaderId, IODispatcher ioDispatcher, IPublisher publisher, CheckpointTag checkpointTag,
		bool stopOnEof, int? stopAfterNEvents, bool resolveLinkTos, IEnumerable<string> streams) {
		var nextPositions = checkpointTag.Streams.ToDictionary(v => v.Key, v => v.Value + 1);

		return new MultiStreamEventReader(
			ioDispatcher, publisher, eventReaderId, _runAs, Phase, streams.ToArray(), nextPositions, resolveLinkTos,
			_timeProvider, stopOnEof, stopAfterNEvents);
	}
}
