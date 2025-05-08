// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable

using System;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Messaging;

namespace KurrentDB.Core.Tests.Fakes;

public class AdHocPublisher : IPublisher {
	public Action<Message> OnPublish { get; set; } = _ => { };

	public void Publish(Message message) {
		OnPublish(message);
	}
}
