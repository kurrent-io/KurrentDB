// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Common.Utils;

namespace EventStore.Core.Messaging;

public class CallbackEnvelope : IEnvelope {
	private readonly Action<Message> _callback;

	public CallbackEnvelope(Action<Message> callback) {
		_callback = callback;
		Ensure.NotNull(callback, "callback");
	}

	public void ReplyWith<T>(T message) where T : Message {
		_callback(message);
	}
}
