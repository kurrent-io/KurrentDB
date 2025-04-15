// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Bus;
using KurrentDB.Core.Messaging;

namespace EventStore.Core.Tests.Bus.Helpers;

public class NoopConsumer : IHandle<Message> {
	public void Handle(Message message) {
	}
}
