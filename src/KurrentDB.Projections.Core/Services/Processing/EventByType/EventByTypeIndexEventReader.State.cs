// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Security.Claims;
using KurrentDB.Core.Data;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Standard;

namespace KurrentDB.Projections.Core.Services.Processing.EventByType;

public partial class EventByTypeIndexEventReader {
	private abstract class State(EventByTypeIndexEventReader reader, ClaimsPrincipal readAs) : IDisposable {
		public abstract void RequestEvents();
		public abstract bool AreEventsRequested();
		public abstract void Dispose();

		protected readonly EventByTypeIndexEventReader _reader = reader;
		protected readonly ClaimsPrincipal _readAs = readAs;

		protected void DeliverEvent(float progress, ResolvedEvent resolvedEvent, TFPos position, KurrentDB.Core.Data.ResolvedEvent pair) {
			if (resolvedEvent.EventOrLinkTargetPosition <= _reader._lastEventPosition)
				return;
			_reader._lastEventPosition = resolvedEvent.EventOrLinkTargetPosition;
			//TODO: this is incomplete.  where reading from TF we need to handle actual deletes

			if (resolvedEvent.IsLinkToDeletedStream && !resolvedEvent.IsLinkToDeletedStreamTombstone)
				return;

			bool isDeletedStreamEvent = StreamDeletedHelper.IsStreamDeletedEventOrLinkToStreamDeletedEvent(
				resolvedEvent, pair.ResolveResult, out var deletedPartitionStreamId);
			if (isDeletedStreamEvent) {
				if (_reader._includeDeletedStreamNotification)
					_reader.Publisher.Publish(
						//TODO: publish both link and event data
						new ReaderSubscriptionMessage.EventReaderPartitionDeleted(
							_reader.EventReaderCorrelationId, deletedPartitionStreamId, source: GetType(), deleteEventOrLinkTargetPosition: position,
							deleteLinkOrEventPosition: resolvedEvent.EventOrLinkTargetPosition,
							positionStreamId: resolvedEvent.PositionStreamId,
							positionEventNumber: resolvedEvent.PositionSequenceNumber));
			} else
				_reader.Publisher.Publish(
					//TODO: publish both link and event data
					new ReaderSubscriptionMessage.CommittedEventDistributed(
						_reader.EventReaderCorrelationId, resolvedEvent,
						_reader.StopOnEof ? null : position.PreparePosition, progress,
						source: GetType()));
		}

		protected void SendNotAuthorized() {
			_reader.SendNotAuthorized();
		}
	}
}
