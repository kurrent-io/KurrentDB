// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System.Collections.Generic;
using System.Threading.Tasks;
using EventStore.Core.Bus;
using EventStore.Core.Messaging;
using EventStore.Core.Services.Monitoring.Stats;

namespace EventStore.Core.Tests.Bus.Helpers;

public class FakeCollectingQueuedHandler : IQueuedHandler {
	public List<Message> PublishedMessages { get; } = [];

	public void Handle(Message message) { }

	public void Publish(Message message) {
		PublishedMessages.Add(message);
	}

	public string Name => string.Empty;
	public Task Start() => Task.CompletedTask;

	public void Stop() { }

	public void RequestStop() { }

	public QueueStats GetStatistics() => null;
}
