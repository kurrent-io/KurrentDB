// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Messaging;

namespace EventStore.Core.Helpers;

[DerivedMessage(CoreMessage.IODispatcher)]
public sealed partial class IODispatcherDelayedMessage : Message {
	private readonly Guid _correlationId;
	private readonly ICorrelatedTimeout _timeout;
	private readonly Guid? _messageCorrelationId;

	public IODispatcherDelayedMessage(Guid correlationId, ICorrelatedTimeout timeout) {
		_timeout = timeout;
		_correlationId = correlationId;
	}

	public IODispatcherDelayedMessage(Guid correlationId, ICorrelatedTimeout timeout, Guid messageCorrelationId) {
		_timeout = timeout;
		_correlationId = correlationId;
		_messageCorrelationId = messageCorrelationId;
	}

	public void Timeout() {
		_timeout.Timeout(_messageCorrelationId ?? Guid.Empty);
	}

	public Guid CorrelationId {
		get { return _correlationId; }
	}

	public Guid? MessageCorrelationId {
		get { return _messageCorrelationId; }
	}
}
