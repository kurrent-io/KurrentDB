// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Messaging;

namespace KurrentDB.Core.Tests.Bus.Helpers;

public class TestHandler<T> : IHandle<T> where T : Message {
	public readonly List<T> HandledMessages = new List<T>();

	public void Handle(T message) {
		HandledMessages.Add(message);
	}
}
