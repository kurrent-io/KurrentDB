// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using EventStore.Core.Bus;

namespace EventStore.Core.Messaging;

// This derivation of RequestResponseDispatcher provides a simple THandler that handles the expected TResponse message.
// Alternate responses (such as NotHandled) are not processed. Timeouts, if any, are implemented elsewhere
// (i.e. in the IODispatcher).
// Allows IODispatcher to use RequestResponseDispatcher in the traditional way.
public sealed class RequestResponseDispatcher<TRequest, TResponse> :
	RequestResponseDispatcher<TRequest, TResponse, AdHocHandlerStruct<TResponse>>
	where TRequest : Message
	where TResponse : Message {

	public RequestResponseDispatcher(
		IPublisher publisher,
		Func<TRequest, Guid> getRequestCorrelationId,
		Func<TResponse, Guid> getResponseCorrelationId,
		IEnvelope defaultReplyEnvelope,
		Func<Guid, Message> cancelMessageFactory = null) : base(
			publisher,
			getRequestCorrelationId,
			getResponseCorrelationId,
			defaultReplyEnvelope,
			cancelMessageFactory) {
	}

	public Guid Publish(TRequest request, Action<TResponse> response, Action timeout = null) =>
		Publish(request, new AdHocHandlerStruct<TResponse>(response, timeout));
}

// Sends requests on the _publisher, keeping hold of a THandler in the _map to handle the response.
// Handles responses via IHandle, correlating them to the THandler in the _map.
public class RequestResponseDispatcher<TRequest, TResponse, THandler> :
	IHandle<TResponse>,
	ICorrelatedTimeout
	where TRequest : Message
	where TResponse : Message
	where THandler : IHandle<TResponse>, IHandleTimeout {

	private readonly Dictionary<Guid, THandler> _map = new Dictionary<Guid, THandler>();
	private readonly IPublisher _publisher;
	private readonly Func<TRequest, Guid> _getRequestCorrelationId;
	private readonly Func<TResponse, Guid> _getResponseCorrelationId;
	private readonly IEnvelope _defaultReplyEnvelope;
	private readonly Func<Guid, Message> _cancelMessageFactory;

	protected RequestResponseDispatcher(
		IPublisher publisher,
		Func<TRequest, Guid> getRequestCorrelationId,
		Func<TResponse, Guid> getResponseCorrelationId,
		IEnvelope defaultReplyEnvelope,
		Func<Guid, Message> cancelMessageFactory = null) {
		_publisher = publisher;
		_getRequestCorrelationId = getRequestCorrelationId;
		_getResponseCorrelationId = getResponseCorrelationId;
		_defaultReplyEnvelope = defaultReplyEnvelope;
		_cancelMessageFactory = cancelMessageFactory;
	}

	public Guid Publish(TRequest request, THandler handler) {
		//TODO: expiration?
		Guid requestCorrelationId;
		lock (_map) {
			requestCorrelationId = _getRequestCorrelationId(request);
			_map.Add(requestCorrelationId, handler);
		}

		_publisher.Publish(request);
		//NOTE: the following condition is required as publishing the message could also process the message 
		// and the correlationId is already invalid here
		lock (_map)
			return _map.ContainsKey(requestCorrelationId) ? requestCorrelationId : Guid.Empty;
	}

	void IHandle<TResponse>.Handle(TResponse message) {
		Handle(message);
	}

	public bool Handle(TResponse message) {
		var correlationId = _getResponseCorrelationId(message);
		var handlerExists = TryRemoveHandler(correlationId, static _ => true, out var handler);

		// We mustn't call the handler inside the lock as we have no
		// knowledge of it's behaviour, which might result in dead locks.
		if (handlerExists) {
			handler.Handle(message);
			return true;
		}

		return false;
	}

	public void Timeout(Guid correlationId) {
		// if we don't handle timeouts then don't remove the handler
		// so that the regular handler can still be called if it comes through.
		// long term i doubt any of the handlers should not deal with timeouts.
		if (TryRemoveHandler(correlationId, static h => h.HandlesTimeout, out var handler)) {
			handler.Timeout();
		}
	}

	protected bool TryRemoveHandler(Guid correlationId, Func<THandler, bool> condition, out THandler handler) {
		lock (_map) {
			var handlerExists = _map.TryGetValue(correlationId, out handler);
			if (handlerExists && condition(handler)) {
				_map.Remove(correlationId);
				return true;
			}
			return false;
		}
	}

	public IEnvelope Envelope {
		get { return _defaultReplyEnvelope; }
	}

	public void Cancel(Guid requestId) {
		if (_cancelMessageFactory != null)
			_publisher.Publish(_cancelMessageFactory(requestId));
		lock (_map)
			_map.Remove(requestId);
	}

	public void CancelAll() {
		Guid[] ids = null;
		lock (_map) {
			_map.Clear();
			if (_cancelMessageFactory != null)
				ids = _map.Keys.ToArray();
		}

		if (ids != null)
			foreach (var id in ids)
				_publisher.Publish(_cancelMessageFactory(id));
	}
}
