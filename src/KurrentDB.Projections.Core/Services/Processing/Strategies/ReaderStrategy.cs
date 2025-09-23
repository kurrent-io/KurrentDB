// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services.TimerService;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using KurrentDB.Projections.Core.Services.Processing.EventByType;
using KurrentDB.Projections.Core.Services.Processing.MultiStream;
using KurrentDB.Projections.Core.Services.Processing.SingleStream;
using KurrentDB.Projections.Core.Services.Processing.Subscriptions;
using KurrentDB.Projections.Core.Services.Processing.TransactionFile;

namespace KurrentDB.Projections.Core.Services.Processing.Strategies;

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

	private readonly ITimeProvider _timeProvider;

	private readonly string _tag;

	public static IReaderStrategy Create(
		string tag,
		int phase,
		IQuerySources sources,
		ITimeProvider timeProvider,
		ClaimsPrincipal runAs) {
		if (!sources.AllStreams && !sources.HasCategories() && !sources.HasStreams())
			throw new InvalidOperationException("None of streams and categories are included");
		if (!sources.AllEvents && !sources.HasEvents())
			throw new InvalidOperationException("None of events are included");
		if (sources.HasStreams() && sources.HasCategories())
			throw new InvalidOperationException("Streams and categories cannot be included in a filter at the same time");
		if (sources.AllStreams && (sources.HasCategories() || sources.HasStreams()))
			throw new InvalidOperationException("Both FromAll and specific categories/streams cannot be set");
		if (sources.AllEvents && sources.HasEvents())
			throw new InvalidOperationException("Both AllEvents and specific event filters cannot be set");
		if (sources.ByStreams && sources.HasStreams())
			throw new InvalidOperationException("foreachStream projections are not supported on stream based sources");

		if (sources.ReorderEventsOption) {
			if (sources.AllStreams)
				throw new InvalidOperationException("Event reordering cannot be used with fromAll()");
			if (!(sources.HasStreams() && sources.Streams.Length > 1)) {
				throw new InvalidOperationException("Event reordering is only available in fromStreams([]) projections");
			}

			if (sources.ProcessingLagOption < 50)
				throw new InvalidOperationException("Event reordering requires processing lag at least of 50ms");
		}

		if (sources.HandlesDeletedNotifications && !sources.ByStreams)
			throw new InvalidOperationException("Deleted stream notifications are only supported with foreachStream()");

		return new ReaderStrategy(
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
		Phase = phase;
		_allStreams = allStreams;
		_categories = categories is { Length: > 0 } ? [..categories] : null;
		_streams = streams is { Length: > 0 } ? [..streams] : null;
		_allEvents = allEvents;
		_includeLinks = includeLinks;
		_events = events is { Length: > 0 } ? [..events] : null;
		_includeStreamDeletedNotification = includeStreamDeletedNotification;
		_processingLag = processingLag.GetValueOrDefault();
		_reorderEvents = reorderEvents;
		_runAs = runAs;

		EventFilter = CreateEventFilter();
		PositionTagger = CreatePositionTagger();
		_timeProvider = timeProvider;
	}

	public bool IsReadingOrderRepeatable => _streams is not { Count: > 1 };

	public EventFilter EventFilter { get; }

	public PositionTagger PositionTagger { get; }

	private int Phase { get; }

	public IReaderSubscription CreateReaderSubscription(
		IPublisher publisher,
		CheckpointTag fromCheckpointTag,
		Guid subscriptionId,
		ReaderSubscriptionOptions readerSubscriptionOptions) {
		return _reorderEvents
			? new EventReorderingReaderSubscription(
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
				readerSubscriptionOptions.EnableContentTypeValidation)
			: new ReaderSubscription(publisher,
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
		Guid eventReaderId,
		IPublisher publisher,
		CheckpointTag checkpointTag,
		bool stopOnEof) {
		switch (_allStreams) {
			case true when _events is { Count: >= 1 }:
				return CreatePausedEventIndexEventReader(
					eventReaderId, publisher, checkpointTag, stopOnEof, true, _events,
					_includeStreamDeletedNotification);
			case true: {
				return new TransactionFileEventReader(publisher, eventReaderId, _runAs,
					new TFPos(checkpointTag.CommitPosition.Value, checkpointTag.PreparePosition.Value), _timeProvider,
					deliverEndOfTFPosition: true, stopOnEof: stopOnEof, resolveLinkTos: false);
			}
		}

		if (_streams is { Count: 1 }) {
			var streamName = checkpointTag.Streams.Keys.First();
			//TODO: handle if not the same
			return CreatePausedStreamEventReader(
				eventReaderId, publisher, checkpointTag, streamName, stopOnEof, resolveLinkTos: true,
				produceStreamDeletes: _includeStreamDeletedNotification);
		}

		if (_categories is { Count: 1 }) {
			var streamName = checkpointTag.Streams.Keys.First();
			return CreatePausedStreamEventReader(
				eventReaderId, publisher, checkpointTag, streamName, stopOnEof, resolveLinkTos: true,
				produceStreamDeletes: _includeStreamDeletedNotification);
		}

		return _streams is { Count: > 1 }
			? CreatePausedMultiStreamEventReader(eventReaderId, publisher, checkpointTag, stopOnEof, true, _streams)
			: throw new NotSupportedException();
	}

	//TODO: clean up $deleted event notification vs $streamDeleted event

	private EventFilter CreateEventFilter() {
		switch (_allStreams) {
			case true when _events is { Count: >= 1 }:
				return new EventByTypeIndexEventFilter(_events);
			//NOTE: a projection cannot handle both stream deleted notifications
			// and real stream tombstone/stream deleted events as they have the same position
			// and thus processing cannot be correctly checkpointed
			case true:
				return new TransactionFileEventFilter(_allEvents, !_includeStreamDeletedNotification, _events, includeLinks: _includeLinks);
		}

		if (_categories is { Count: 1 })
			return new CategoryEventFilter(_categories.First(), _allEvents, _events);
		if (_categories != null)
			throw new NotSupportedException();
		return _streams switch {
			{ Count: 1 } => new StreamEventFilter(_streams.First(), _allEvents, _events),
			{ Count: > 1 } => new MultiStreamEventFilter(_streams, _allEvents, _events),
			_ => throw new NotSupportedException()
		};
	}

	private PositionTagger CreatePositionTagger() {
		switch (_allStreams) {
			case true when _events is { Count: >= 1 }:
				return new EventByTypeIndexPositionTagger(Phase, _events.ToArray(), _includeStreamDeletedNotification);
			case true when _reorderEvents:
				return new PreparePositionTagger(Phase);
			case true:
				return new TransactionFilePositionTagger(Phase);
		}

		if (_categories is { Count: 1 })
			return new StreamPositionTagger(Phase, $"$ce-{_categories.First()}");
		if (_categories != null)
			throw new NotSupportedException();
		return _streams switch {
			{ Count: 1 } => new StreamPositionTagger(Phase, _streams.First()),
			{ Count: > 1 } => new MultiStreamPositionTagger(Phase, _streams.ToArray()),
			_ => throw new NotSupportedException()
		};
		//TODO: consider passing projection phase from outside (above)
	}

	private StreamEventReader CreatePausedStreamEventReader(
		Guid eventReaderId,
		IPublisher publisher,
		CheckpointTag checkpointTag,
		string streamName,
		bool stopOnEof,
		bool resolveLinkTos,
		bool produceStreamDeletes) {
		var lastProcessedSequenceNumber = checkpointTag.Streams.Values.First();
		var fromSequenceNumber = lastProcessedSequenceNumber + 1;
		return new(publisher, eventReaderId, _runAs, streamName, fromSequenceNumber, _timeProvider, resolveLinkTos, produceStreamDeletes, stopOnEof);
	}

	private EventByTypeIndexEventReader CreatePausedEventIndexEventReader(
		Guid eventReaderId,
		IPublisher publisher,
		CheckpointTag checkpointTag,
		bool stopOnEof,
		bool resolveLinkTos,
		IEnumerable<string> eventTypes,
		bool includeStreamDeletedNotification) {
		var et = eventTypes.ToArray();
		//NOTE: just optimization - anyway if reading from TF events may reappear
		long p;
		var nextPositions = et.ToDictionary(v => $"$et-{v}", v => checkpointTag.Streams.TryGetValue(v, out p) ? p + 1 : 0);

		if (includeStreamDeletedNotification)
			nextPositions.Add("$et-$deleted", checkpointTag.Streams.TryGetValue("$deleted", out p) ? p + 1 : 0);

		return new(publisher, eventReaderId, _runAs, et, includeStreamDeletedNotification,
			checkpointTag.Position, nextPositions, resolveLinkTos, _timeProvider, stopOnEof);
	}

	private MultiStreamEventReader CreatePausedMultiStreamEventReader(
		Guid eventReaderId,
		IPublisher publisher,
		CheckpointTag checkpointTag,
		bool stopOnEof,
		bool resolveLinkTos,
		IEnumerable<string> streams) {
		var nextPositions = checkpointTag.Streams.ToDictionary(v => v.Key, v => v.Value + 1);

		return new(publisher, eventReaderId, _runAs, Phase, streams.ToArray(), nextPositions, resolveLinkTos, _timeProvider, stopOnEof);
	}
}
