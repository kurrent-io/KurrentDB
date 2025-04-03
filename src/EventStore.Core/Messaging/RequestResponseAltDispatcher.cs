// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Bus;
using EventStore.Core.Helpers;
using EventStore.Core.Messages;

namespace EventStore.Core.Messaging;

// Just a typedef
public class ReadDispatcher :
	RequestResponseAltDispatcher<
		ClientMessage.ReadStreamEventsBackward,
		ClientMessage.ReadStreamEventsBackwardCompleted,
		ClientMessage.NotHandled,
		IReadStreamEventsBackwardHandler> {
	public ReadDispatcher(
		IPublisher publisher,
		Func<ClientMessage.ReadStreamEventsBackward, Guid> getRequestCorrelationId,
		Func<ClientMessage.ReadStreamEventsBackwardCompleted, Guid> getResponseCorrelationId,
		Func<ClientMessage.NotHandled, Guid> getResponseAltCorrelationId,
		IEnvelope defaultReplyEnvelope,
		Func<Guid, Message> cancelMessageFactory = null) :
			base(publisher, getRequestCorrelationId, getResponseCorrelationId,
				getResponseAltCorrelationId, defaultReplyEnvelope, cancelMessageFactory) {
	}
}

// This derivation of RequestResponseDispatcher handles two different response messages
// Useful if the system might respond to the request with a message like NotHandled and we want to know about it.
public class RequestResponseAltDispatcher<TRequest, TResponse, TResponseAlt, THandler> :
	RequestResponseDispatcher<TRequest, TResponse, THandler>,
	IHandle<TResponseAlt>
	where TRequest : Message
	where TResponse : Message
	where TResponseAlt : Message
	where THandler : IHandle<TResponse>, IHandleAlt<TResponseAlt>, IHandleTimeout {

	private readonly Func<TResponseAlt, Guid> _getResponseAltCorrelationId;

	public RequestResponseAltDispatcher(
		IPublisher publisher,
		Func<TRequest, Guid> getRequestCorrelationId,
		Func<TResponse, Guid> getResponseCorrelationId,
		Func<TResponseAlt, Guid> getResponseAltCorrelationId,
		IEnvelope defaultReplyEnvelope,
		Func<Guid, Message> cancelMessageFactory = null) : base(
			publisher,
			getRequestCorrelationId,
			getResponseCorrelationId,
			defaultReplyEnvelope,
			cancelMessageFactory) {
		_getResponseAltCorrelationId = getResponseAltCorrelationId;
	}

	void IHandle<TResponseAlt>.Handle(TResponseAlt message) {
		var correlationId = _getResponseAltCorrelationId(message);

		// if we don't handle the alternative message, then don't remove the handler
		// so that it can be called on timeout
		var handlerExists = TryRemoveHandler(correlationId, static h => h.HandlesAlt, out var handler);

		if (handlerExists) {
			handler.Handle(message);
		}
	}
}
