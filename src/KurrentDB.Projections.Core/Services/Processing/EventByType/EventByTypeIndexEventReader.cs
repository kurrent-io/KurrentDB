// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Services.TimerService;
using AwakeServiceMessage = KurrentDB.Core.Services.AwakeReaderService.AwakeServiceMessage;

namespace KurrentDB.Projections.Core.Services.Processing.EventByType;

public partial class EventByTypeIndexEventReader : EventReader {
	private const int MaxReadCount = 50;
	private readonly HashSet<string> _eventTypes;
	private readonly bool _resolveLinkTos;
	private readonly bool _includeDeletedStreamNotification;
	private readonly ITimeProvider _timeProvider;

	private State _state;
	private TFPos _lastEventPosition;
	private readonly Dictionary<string, long> _fromPositions;
	private readonly Dictionary<string, string> _streamToEventType;
	private long _lastPosition;

	public EventByTypeIndexEventReader(
		IPublisher publisher,
		Guid eventReaderCorrelationId,
		ClaimsPrincipal readAs,
		string[] eventTypes,
		bool includeDeletedStreamNotification,
		TFPos fromTfPosition,
		Dictionary<string, long> fromPositions,
		bool resolveLinkTos,
		ITimeProvider timeProvider,
		bool stopOnEof = false)
		: base(publisher, eventReaderCorrelationId, readAs, stopOnEof) {
		ArgumentNullException.ThrowIfNull(eventTypes);
		ArgumentNullException.ThrowIfNull(timeProvider);
		if (eventTypes.Length == 0)
			throw new ArgumentException("empty", nameof(eventTypes));

		_includeDeletedStreamNotification = includeDeletedStreamNotification;
		_timeProvider = timeProvider;
		_eventTypes = new(eventTypes);
		if (includeDeletedStreamNotification)
			_eventTypes.Add("$deleted");
		_streamToEventType = eventTypes.ToDictionary(v => $"$et-{v}", v => v);
		_lastEventPosition = fromTfPosition;
		_resolveLinkTos = resolveLinkTos;

		ValidateTag(fromPositions);

		_fromPositions = fromPositions;
		_state = new IndexBased(_eventTypes, this, readAs);
	}

	private void ValidateTag(Dictionary<string, long> fromPositions) {
		if (_eventTypes.Count != fromPositions.Count)
			throw new ArgumentException("Number of streams does not match", nameof(fromPositions));

		foreach (var stream in _streamToEventType.Keys.Where(stream => !fromPositions.ContainsKey(stream))) {
			throw new ArgumentException($"The '{stream}' stream position has not been set", nameof(fromPositions));
		}
	}

	public override void Dispose() {
		_state.Dispose();
		base.Dispose();
	}

	protected override void RequestEvents() {
		if (Disposed || PauseRequested || Paused)
			return;
		_state.RequestEvents();
	}

	protected override bool AreEventsRequested() => _state.AreEventsRequested();

	private void PublishIORequest(bool delay, Message readEventsForward, Message timeoutMessage, Guid correlationId) {
		if (delay) {
			Publisher.Publish(
				new AwakeServiceMessage.SubscribeAwake(Publisher, correlationId, null, new(_lastPosition, _lastPosition),
					readEventsForward));
		} else {
			Publisher.Publish(readEventsForward);
			Publisher.Publish(timeoutMessage);
		}
	}

	private void UpdateNextStreamPosition(string eventStreamId, long nextPosition) {
		if (!_fromPositions.TryGetValue(eventStreamId, out var streamPosition))
			streamPosition = -1;
		if (nextPosition > streamPosition)
			_fromPositions[eventStreamId] = nextPosition;
	}

	private void DoSwitch(TFPos lastKnownIndexCheckpointPosition) {
		if (Paused || PauseRequested || Disposed)
			throw new InvalidOperationException("_paused || _pauseRequested || _disposed");

		// skip reading TF up to last know index checkpoint position
		// as we could only gethere if there is no more indexed events before this point
		if (lastKnownIndexCheckpointPosition > _lastEventPosition)
			_lastEventPosition = lastKnownIndexCheckpointPosition;

		_state = new TfBased(_timeProvider, this, _lastEventPosition, Publisher, ReadAs);
		_state.RequestEvents();
	}
}
