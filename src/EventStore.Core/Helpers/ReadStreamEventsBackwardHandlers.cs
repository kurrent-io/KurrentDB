// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Bus;
using EventStore.Core.Messages;
using EventStore.Common.Utils;
using static EventStore.Core.Helpers.IODispatcher;

namespace EventStore.Core.Helpers;

public interface IReadStreamEventsBackwardHandler :
	IHandle<ClientMessage.ReadStreamEventsBackwardCompleted>,
	IHandleAlt<ClientMessage.NotHandled>,
	IHandleTimeout {
}

public static class ReadStreamEventsBackwardHandlers {
	// Adds and removes from a tracker so it knows what is outstanding
	public class Tracking : IReadStreamEventsBackwardHandler {
		private readonly Guid _correlationId;
		private readonly RequestTracking _requestTracker;
		private readonly IReadStreamEventsBackwardHandler _wrapped;

		public Tracking(
			Guid correlationId,
			RequestTracking requestTracker,
			IReadStreamEventsBackwardHandler wrapped) {

			Ensure.NotNull(requestTracker, nameof(requestTracker));
			Ensure.NotNull(wrapped, nameof(wrapped));

			_correlationId = correlationId;
			_requestTracker = requestTracker;
			_wrapped = wrapped;

			_requestTracker.AddPendingRead(correlationId);
		}

		public bool HandlesAlt => _wrapped.HandlesAlt;
		public bool HandlesTimeout => _wrapped.HandlesTimeout;

		public void Handle(ClientMessage.ReadStreamEventsBackwardCompleted message) {
			if (_requestTracker.RemovePendingRead(message.CorrelationId)) {
				_wrapped.Handle(message);
			}
		}

		public void Handle(ClientMessage.NotHandled message) {
			if (_requestTracker.RemovePendingRead(message.CorrelationId)) {
				_wrapped.Handle(message);
			}
		}

		public void Timeout() {
			if (_requestTracker.RemovePendingRead(_correlationId)) {
				_wrapped.Timeout();
			}
		}
	}

	// todo: move away from this and towards bespoke implementations (like AuthReadResponseHandler) to avoid having to
	// allocate all these delegates
	public class AdHoc : IReadStreamEventsBackwardHandler {
		private readonly Action<ClientMessage.ReadStreamEventsBackwardCompleted> _handled;
		private readonly Action<ClientMessage.NotHandled> _notHandled;
		private readonly Action _timedout;

		public AdHoc(
			Action<ClientMessage.ReadStreamEventsBackwardCompleted> handled,
			Action<ClientMessage.NotHandled> notHandled,
			Action timedout) {

			Ensure.NotNull(handled, nameof(handled));

			HandlesAlt = notHandled is not null;
			HandlesTimeout = timedout is not null;
			_handled = handled;
			_notHandled = notHandled.OrNoOp();
			_timedout = timedout.OrNoOp();
		}

		public bool HandlesAlt { get; }
		public bool HandlesTimeout { get; }

		public void Handle(ClientMessage.ReadStreamEventsBackwardCompleted message) {
			_handled(message);
		}

		public void Handle(ClientMessage.NotHandled message) {
			_notHandled(message);
		}

		public void Timeout() {
			_timedout();
		}
	}

	// Assumes the request will eventually receive a reponse of the expected type
	// And not be dropped due to timeout or responded to with a different message
	public class Optimistic : AdHoc {
		public Optimistic(
			Action<ClientMessage.ReadStreamEventsBackwardCompleted> handle) : base(
				handle, null, null) {
		}
	}
}
