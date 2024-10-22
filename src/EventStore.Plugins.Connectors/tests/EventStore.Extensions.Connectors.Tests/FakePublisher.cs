// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using EventStore.Core.Bus;
using EventStore.Core.Messaging;

namespace EventStore.Extensions.Connectors.Tests;

public class FakePublisher : IPublisher {
    public List<Message> Messages { get; }

    public FakePublisher() {
        Messages = new List<Message>();
    }

    public void Publish(Message message) {
        Messages.Add(message);
    }
}