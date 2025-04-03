// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Core.Tests.Infrastructure;

public interface IRandTestFinishCondition {
	bool Done { get; }
	bool Success { get; }

	void Process(int iteration, RandTestQueueItem item);
	void Log();
}
