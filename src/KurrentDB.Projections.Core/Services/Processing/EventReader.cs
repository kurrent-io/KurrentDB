// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Security.Claims;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Projections.Core.Messages;

namespace KurrentDB.Projections.Core.Services.Processing;

public abstract class EventReader : IEventReader {
	protected readonly Guid EventReaderCorrelationId;
	protected readonly IPublisher _publisher;
	protected readonly bool _stopOnEof;
	protected bool _disposed;
	private bool _startingSent;

	protected EventReader(IPublisher publisher, Guid eventReaderCorrelationId, ClaimsPrincipal readAs, bool stopOnEof) {
		ArgumentNullException.ThrowIfNull(publisher);
		if (eventReaderCorrelationId == Guid.Empty)
			throw new ArgumentException(null, nameof(eventReaderCorrelationId));
		_publisher = publisher;
		EventReaderCorrelationId = eventReaderCorrelationId;
		ReadAs = readAs;
		_stopOnEof = stopOnEof;
	}

	protected bool PauseRequested { get; private set; } = true;

	protected bool Paused { get; private set; } = true;

	protected ClaimsPrincipal ReadAs { get; }

	public void Resume() {
		if (_disposed)
			throw new InvalidOperationException("Disposed");
		if (!PauseRequested)
			throw new InvalidOperationException("Is not paused");
		if (!Paused) {
			PauseRequested = false;
			return;
		}

		Paused = false;
		PauseRequested = false;
		RequestEvents();
	}

	public void Pause() {
		if (_disposed)
			return; // due to possible self disposed

		if (PauseRequested)
			throw new InvalidOperationException("Pause has been already requested");
		PauseRequested = true;
		if (!AreEventsRequested())
			Paused = true;
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

	protected void SendPartitionDeleted_WhenReadingDataStream(
		string partition, TFPos? deletedLinkOrEventPosition, TFPos? deletedEventPosition,
		string positionStreamId, int? positionEventNumber) {
		if (_disposed)
			return;
		_publisher.Publish(
			new ReaderSubscriptionMessage.EventReaderPartitionDeleted(
				EventReaderCorrelationId, partition, deletedLinkOrEventPosition,
				deletedEventPosition, positionStreamId, positionEventNumber));
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
			? msg.TfLastCommitPosition == -1 ? null : msg.TfLastCommitPosition
			: null;
	}

	protected void PauseOrContinueProcessing() {
		if (_disposed)
			return;
		if (PauseRequested)
			Paused = !AreEventsRequested();
		else
			RequestEvents();
	}

	private void SendStarting(long startingLastCommitPosition) {
		_publisher.Publish(new ReaderSubscriptionMessage.EventReaderStarting(EventReaderCorrelationId, startingLastCommitPosition));
	}

	protected void NotifyIfStarting(long startingLastCommitPosition) {
		if (!_startingSent) {
			_startingSent = true;
			SendStarting(startingLastCommitPosition);
		}
	}
}
