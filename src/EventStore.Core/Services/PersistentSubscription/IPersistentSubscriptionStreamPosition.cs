// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;

namespace EventStore.Core.Services.PersistentSubscription;

public interface IPersistentSubscriptionStreamPosition : IEquatable<IPersistentSubscriptionStreamPosition>, IComparable<IPersistentSubscriptionStreamPosition> {
	bool IsSingleStreamPosition { get; }
	long StreamEventNumber { get; }
	bool IsAllStreamPosition { get; }
	(long Commit, long Prepare) TFPosition { get; }
	bool IsLivePosition { get; }
	string ToString();
}
