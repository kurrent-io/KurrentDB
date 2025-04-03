// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Security.Claims;
using EventStore.Core.Data;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Standard;

namespace EventStore.Projections.Core.Services.Processing.EventByType;

public partial class EventByTypeIndexEventReader
{
	private abstract class State : IDisposable {
		public abstract void RequestEvents();
		public abstract bool AreEventsRequested();
		public abstract void Dispose();

		protected readonly EventByTypeIndexEventReader _reader;
		protected readonly ClaimsPrincipal _readAs;

		protected State(EventByTypeIndexEventReader reader, ClaimsPrincipal readAs) {
			_reader = reader;
			_readAs = readAs;
		}

		protected void DeliverEvent(float progress, ResolvedEvent resolvedEvent, TFPos position,
			EventStore.Core.Data.ResolvedEvent pair) {
			if (resolvedEvent.EventOrLinkTargetPosition <= _reader._lastEventPosition)
				return;
			_reader._lastEventPosition = resolvedEvent.EventOrLinkTargetPosition;
			//TODO: this is incomplete.  where reading from TF we need to handle actual deletes

			string deletedPartitionStreamId;


			if (resolvedEvent.IsLinkToDeletedStream && !resolvedEvent.IsLinkToDeletedStreamTombstone)
				return;

			bool isDeletedStreamEvent = StreamDeletedHelper.IsStreamDeletedEventOrLinkToStreamDeletedEvent(
				resolvedEvent, pair.ResolveResult, out deletedPartitionStreamId);
			if (isDeletedStreamEvent) {
				var deletedPartition = deletedPartitionStreamId;

				if (_reader._includeDeletedStreamNotification)
					_reader._publisher.Publish(
						//TODO: publish both link and event data
						new ReaderSubscriptionMessage.EventReaderPartitionDeleted(
							_reader.EventReaderCorrelationId, deletedPartition, source: this.GetType(),
							lastEventNumber: -1, deleteEventOrLinkTargetPosition: position,
							deleteLinkOrEventPosition: resolvedEvent.EventOrLinkTargetPosition,
							positionStreamId: resolvedEvent.PositionStreamId,
							positionEventNumber: resolvedEvent.PositionSequenceNumber));
			} else
				_reader._publisher.Publish(
					//TODO: publish both link and event data
					new ReaderSubscriptionMessage.CommittedEventDistributed(
						_reader.EventReaderCorrelationId, resolvedEvent,
						_reader._stopOnEof ? (long?)null : position.PreparePosition, progress,
						source: this.GetType()));
		}

		protected void SendNotAuthorized() {
			_reader.SendNotAuthorized();
		}
	}
}
