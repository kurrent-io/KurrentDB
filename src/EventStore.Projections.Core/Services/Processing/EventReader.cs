// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Security.Claims;
using EventStore.Core.Bus;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services.Processing.Checkpointing;

namespace EventStore.Projections.Core.Services.Processing;

public abstract class EventReader : IEventReader {
	protected readonly Guid EventReaderCorrelationId;
	private readonly ClaimsPrincipal _readAs;
	protected readonly IPublisher _publisher;

	protected readonly bool _stopOnEof;
	private bool _paused = true;
	private bool _pauseRequested = true;
	protected bool _disposed;
	private bool _startingSent;

	protected EventReader(IPublisher publisher, Guid eventReaderCorrelationId, ClaimsPrincipal readAs, bool stopOnEof) {
		if (publisher == null) throw new ArgumentNullException("publisher");
		if (eventReaderCorrelationId == Guid.Empty)
			throw new ArgumentException("eventReaderCorrelationId");
		_publisher = publisher;
		EventReaderCorrelationId = eventReaderCorrelationId;
		_readAs = readAs;
		_stopOnEof = stopOnEof;
	}

	protected bool PauseRequested {
		get { return _pauseRequested; }
	}

	protected bool Paused {
		get { return _paused; }
	}

	protected ClaimsPrincipal ReadAs {
		get { return _readAs; }
	}

	public void Resume() {
		if (_disposed) throw new InvalidOperationException("Disposed");
		if (!_pauseRequested)
			throw new InvalidOperationException("Is not paused");
		if (!_paused) {
			_pauseRequested = false;
			return;
		}

		_paused = false;
		_pauseRequested = false;
//            _logger.Trace("Resuming event distribution {eventReaderCorrelationId} at '{at}'", EventReaderCorrelationId, FromAsText());
		RequestEvents();
	}

	public void Pause() {
		if (_disposed)
			return; // due to possible self disposed

		if (_pauseRequested)
			throw new InvalidOperationException("Pause has been already requested");
		_pauseRequested = true;
		if (!AreEventsRequested())
			_paused = true;
//            _logger.Trace("Pausing event distribution {eventReaderCorrelationId} at '{at}'", EventReaderCorrelationId, FromAsText());
	}

	public virtual void Dispose() {
		_disposed = true;
	}

	protected abstract bool AreEventsRequested();
	protected abstract void RequestEvents();

	protected void SendEof() {
		if (_stopOnEof) {
			_publisher.Publish(new ReaderSubscriptionMessage.EventReaderEof(EventReaderCorrelationId));
			Dispose();
		}
	}

	protected void SendPartitionEof(string partition, CheckpointTag preTagged) {
		if (_disposed)
			return;
		_publisher.Publish(
			new ReaderSubscriptionMessage.EventReaderPartitionEof(EventReaderCorrelationId, partition, preTagged));
	}

	protected void SendPartitionDeleted_WhenReadingDataStream(
		string partition, long? lastEventNumber, TFPos? deletedLinkOrEventPosition, TFPos? deletedEventPosition,
		string positionStreamId,
		int? positionEventNumber, CheckpointTag preTagged = null) {
		if (_disposed)
			return;
		_publisher.Publish(
			new ReaderSubscriptionMessage.EventReaderPartitionDeleted(
				EventReaderCorrelationId, partition, lastEventNumber, deletedLinkOrEventPosition,
				deletedEventPosition, positionStreamId, positionEventNumber, preTagged));
	}

	public void SendNotAuthorized() {
		if (_disposed)
			return;
		_publisher.Publish(new ReaderSubscriptionMessage.EventReaderNotAuthorized(EventReaderCorrelationId));
		Dispose();
	}

	protected static long? GetLastCommitPositionFrom(ClientMessage.ReadStreamEventsForwardCompleted msg) {
		return (msg.IsEndOfStream
		        || msg.Result == ReadStreamResult.NoStream
		        || msg.Result == ReadStreamResult.StreamDeleted)
			? (msg.TfLastCommitPosition == -1 ? (long?)null : msg.TfLastCommitPosition)
			: (long?)null;
	}

	protected void PauseOrContinueProcessing() {
		if (_disposed)
			return;
		if (_pauseRequested)
			_paused = !AreEventsRequested();
		else
			RequestEvents();
	}

	private void SendStarting(long startingLastCommitPosition) {
		_publisher.Publish(
			new ReaderSubscriptionMessage.EventReaderStarting(EventReaderCorrelationId,
				startingLastCommitPosition));
	}

	protected void NotifyIfStarting(long startingLastCommitPosition) {
		if (!_startingSent) {
			_startingSent = true;
			SendStarting(startingLastCommitPosition);
		}
	}
}
