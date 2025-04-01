// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

namespace EventStore.POC.IO.Core;

public abstract class ResponseException : Exception {
	protected ResponseException() : base() { }
	protected ResponseException(string? message) : base(message) { }
	protected ResponseException(string? message, Exception inner) : base(message, inner) { }

	/// Request was not authorized
	public class AccessDenied : ResponseException {
		public AccessDenied(string message) : base(message) { }
		public AccessDenied(Exception inner) : base(null, inner) { }
	}

	public class NotAuthenticated : ResponseException {
		public NotAuthenticated(Exception inner) : base(null, inner) { }
	}

	public class NotLeader : ResponseException {
		public NotLeader() { }
		public NotLeader(Exception inner) : base(null, inner) { }
	}

	public class SubscriptionDropped : ResponseException {
		public SubscriptionDropped() { }
		public SubscriptionDropped(string stream, string reason) :
			base($"Subscription to {stream} was dropped {reason}.") { }
	}
	
	public class ServerBusy : ResponseException {
		public ServerBusy() { }
		public ServerBusy(Exception inner) : base(null, inner) { }
	}

	public class ServerNotReady : ResponseException {
		public ServerNotReady() { }
		public ServerNotReady(Exception inner) : base(null, inner) { }
	}

	public class StreamDeleted : ResponseException {
		public StreamDeleted(string message) : base(message) { }
		public StreamDeleted(Exception inner) : base(null, inner) { }
	}

	public class StreamNotFound : ResponseException {
		public StreamNotFound(string message) : base(message) {
		}
	}

	public class Timeout : ResponseException {
		public Timeout(string message) : base(message) { }
		public Timeout(Exception inner) : base(null, inner) { }
	}

	public class UnexpectedError : ResponseException {
		public UnexpectedError(string message) : base(message) { }
		public UnexpectedError(Exception inner) : base(null, inner) { }
	}

	public class WrongExpectedVersion : ResponseException {
		public WrongExpectedVersion(string message) : base(message) { }
		public WrongExpectedVersion(Exception inner) : base(null, inner) { }
	}
}
