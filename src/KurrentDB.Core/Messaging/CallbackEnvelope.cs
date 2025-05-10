// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable

using System;
using KurrentDB.Common.Utils;

namespace KurrentDB.Core.Messaging;

public class CallbackEnvelope : IEnvelope {
	private readonly Action<Message> _callback;

	public CallbackEnvelope(Action<Message> callback) {
		_callback = callback;
		Ensure.NotNull(callback, "callback");
	}

	public void ReplyWith<T>(T message) where T : Message {
		_callback(message);
	}

	public static CallbackEnvelope Create(Action<Message> callback) =>
		new(callback);

	public static CallbackEnvelope<TArg> Create<TArg>(TArg arg, Action<TArg, Message> callback) =>
		new(arg, callback);
}

public class CallbackEnvelope<TArg>(TArg arg, Action<TArg, Message> callback) : IEnvelope {
	public void ReplyWith<T>(T message) where T : Message {
		callback(arg, message);
	}
}
