// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Messaging;

namespace KurrentDB.Core.Tests.Bus.Helpers;

public class TestMultiHandler : IHandle<TestMessage>, IHandle<TestMessage2>, IHandle<TestMessage3> {
	public readonly List<Message> HandledMessages = new List<Message>();

	public void Handle(TestMessage message) {
		HandledMessages.Add(message);
	}

	public void Handle(TestMessage2 message) {
		HandledMessages.Add(message);
	}

	public void Handle(TestMessage3 message) {
		HandledMessages.Add(message);
	}
}
