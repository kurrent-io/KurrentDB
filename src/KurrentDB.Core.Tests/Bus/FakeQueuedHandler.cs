// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading.Tasks;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Services.Monitoring.Stats;

namespace KurrentDB.Core.Tests.Bus;

public class FakeQueuedHandler : IQueuedHandler {
	public string Name => string.Empty;
	public void Handle(Message message) { }
	public void Publish(Message message) { }
	public void Start() { }
	public Task Stop() => Task.CompletedTask;
	public void RequestStop() { }
	public QueueStats GetStatistics() => null;
}
