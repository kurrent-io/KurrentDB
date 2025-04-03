// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using EventStore.Core.Messaging;

namespace EventStore.Core.Tests.Services.Replication;

public class FakeEnvelope : IEnvelope {
	public List<Message> Replies = new List<Message>();

	public void ReplyWith<T>(T message) where T : Message {
		Replies.Add(message);
	}
}
