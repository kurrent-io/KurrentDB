// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading.Tasks;

namespace EventStore.Core.Bus;

public interface IQueuedHandler : IPublisher {
	void Start();
	Task Stop();
	void RequestStop();
}
