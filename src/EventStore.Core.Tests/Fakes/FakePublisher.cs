// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using EventStore.Core.Bus;
using EventStore.Core.Messaging;

namespace EventStore.Core.Tests.Fakes;

public class FakePublisher : IPublisher {
	public readonly List<Message> Messages;

	public FakePublisher() {
		Messages = new List<Message>();
	}

	public void Publish(Message message) {
		Messages.Add(message);
	}
}
