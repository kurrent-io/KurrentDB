// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

namespace EventStore.Core.Messaging;

public struct AdHocCorrelatedTimeout : ICorrelatedTimeout {
	private readonly Action<Guid> _timeout;

	public AdHocCorrelatedTimeout(Action<Guid> timeout) {
		_timeout = timeout;
	}

	public void Timeout(Guid correlationId) {
		_timeout(correlationId);
	}
}
