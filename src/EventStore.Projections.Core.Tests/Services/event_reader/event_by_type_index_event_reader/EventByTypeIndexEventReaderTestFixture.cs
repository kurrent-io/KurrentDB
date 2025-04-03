// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Tests.Services.core_projection;
using ResolvedEvent = EventStore.Core.Data.ResolvedEvent;

public abstract class EventByTypeIndexEventReaderTestFixture<TLogFormat, TStreamId> : TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
	public Guid CompleteForwardStreamRead(string streamId, Guid corrId, params ResolvedEvent[] events) {
		var lastEventNumber = events != null && events.Length > 0 ? events.Last().Event.EventNumber : 0;
		var message = _consumer.HandledMessages.OfType<ClientMessage.ReadStreamEventsForward>()
			.Last(x => x.EventStreamId == streamId);
		message.Envelope.ReplyWith(
			new ClientMessage.ReadStreamEventsForwardCompleted(
				corrId == Guid.Empty ? message.CorrelationId : corrId, streamId, 0, 100, ReadStreamResult.Success,
				events, null, false, "", lastEventNumber + 1, lastEventNumber, true, 200));
		return message.CorrelationId;
	}

	public Guid CompleteForwardAllStreamRead(Guid corrId, params ResolvedEvent[] events) {
		var message = _consumer.HandledMessages.OfType<ClientMessage.ReadAllEventsForward>().Last();
		message.Envelope.ReplyWith(
			new ClientMessage.ReadAllEventsForwardCompleted(
				corrId == Guid.Empty ? message.CorrelationId : corrId, ReadAllResult.Success,
				"", events, null, false, 100, new TFPos(200, 150), new TFPos(500, -1), new TFPos(100, 50), 500));
		return message.CorrelationId;
	}

	public Guid CompleteBackwardStreamRead(string streamId, Guid corrId, params ResolvedEvent[] events) {
		var lastEventNumber = events != null && events.Length > 0 ? events.Last().Event.EventNumber : 0;
		var message = _consumer.HandledMessages.OfType<ClientMessage.ReadStreamEventsBackward>()
			.Last(x => x.EventStreamId == streamId);
		message.Envelope.ReplyWith(
			new ClientMessage.ReadStreamEventsBackwardCompleted(
				corrId == Guid.Empty ? message.CorrelationId : corrId, streamId, 0, 100, ReadStreamResult.Success,
				new ResolvedEvent[] { }, null, false, "", lastEventNumber + 1, lastEventNumber, true, 200));
		return message.CorrelationId;
	}

	public Guid TimeoutRead(string streamId, Guid corrId) {
		var timeoutMessage = _consumer.HandledMessages
			.OfType<EventStore.Core.Services.TimerService.TimerMessage.Schedule>().Last(x =>
				((ProjectionManagementMessage.Internal.ReadTimeout)x.ReplyMessage).StreamId == streamId);
		var correlationId = ((ProjectionManagementMessage.Internal.ReadTimeout)timeoutMessage.ReplyMessage)
			.CorrelationId;
		correlationId = corrId == Guid.Empty ? correlationId : corrId;
		timeoutMessage.Envelope.ReplyWith(
			new ProjectionManagementMessage.Internal.ReadTimeout(corrId == Guid.Empty ? correlationId : corrId,
				streamId));
		return correlationId;
	}

	protected static string TFPosToMetadata(TFPos tfPos) {
		return string.Format(@"{{""$c"":{0},""$p"":{1}}}", tfPos.CommitPosition, tfPos.PreparePosition);
	}
}
