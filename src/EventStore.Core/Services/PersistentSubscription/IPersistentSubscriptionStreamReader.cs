// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using EventStore.Core.Data;

namespace EventStore.Core.Services.PersistentSubscription;

public interface IPersistentSubscriptionStreamReader {
	void BeginReadEvents(IPersistentSubscriptionEventSource eventSource,
		IPersistentSubscriptionStreamPosition startPosition, int countToLoad, int batchSize, int maxWindowSize,
		bool resolveLinkTos, bool skipFirstEvent,
		Action<IReadOnlyList<ResolvedEvent>, IPersistentSubscriptionStreamPosition, bool> onEventsFound,
		Action<IPersistentSubscriptionStreamPosition, long> onEventsSkipped,
		Action<string> onError);
}
