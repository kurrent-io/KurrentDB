// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable CheckNamespace

using KurrentDB.Core.Messaging;

namespace KurrentDB.Core;

class AsyncCallbackEnvelope(Func<Message, Task> callback) : IEnvelope {
	Func<Message, Task> Callback { get; } = callback;

	public void ReplyWith<T>(T message) where T : Message => Callback(message).ConfigureAwait(true).GetAwaiter().GetResult();

	public static AsyncCallbackEnvelope Create(Func<Message, Task> callback) => new(callback);
}
