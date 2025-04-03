// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Messaging;

namespace EventStore.Core.Tests.Bus.Helpers;

public class DeferredExecutionTestMessage : Message {
	private readonly Action _action;

	public DeferredExecutionTestMessage(Action action) {
		if (action == null)
			throw new ArgumentNullException("action");
		_action = action;
	}

	public void Execute() {
		_action();
	}
}

public class ExecutableTestMessage : Message {
	private readonly Action _action;

	public ExecutableTestMessage(Action action) {
		if (action == null)
			throw new ArgumentNullException("action");
		_action = action;
	}

	public void Execute() {
		_action();
	}
}
