// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;

namespace EventStore.Core.Tests.Infrastructure;

public interface IRandTestItemProcessor {
	IEnumerable<RandTestQueueItem> ProcessedItems { get; }

	void Process(int iteration, RandTestQueueItem item);
	void LogMessages();
}
