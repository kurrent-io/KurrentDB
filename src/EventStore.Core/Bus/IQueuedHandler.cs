// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System.Threading.Tasks;
using EventStore.Core.Services.Monitoring.Stats;

namespace EventStore.Core.Bus;

public interface IQueuedHandler : IPublisher {
	string Name { get; }
	Task Start();
	void Stop();

	void RequestStop();

	QueueStats GetStatistics();
}
