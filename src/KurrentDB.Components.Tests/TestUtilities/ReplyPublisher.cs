// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Bus;
using KurrentDB.Core.Messaging;

namespace KurrentDB.Components.Tests.TestUtilities;

// Minimal IPublisher test double. The Blazor services publish a request message carrying a
// TcsEnvelope and await its reply; this routes each published message to a supplied handler,
// which can reply on the message's envelope (msg.Envelope.ReplyWith(...)) to complete that await.
// Messages the handler ignores are simply dropped — the corresponding service call then times out
// on its own CancellationToken and the component swallows it, leaving its state empty.
public sealed class ReplyPublisher(Action<Message> onPublish) : IPublisher {
	public void Publish(Message message) => onPublish(message);
}
