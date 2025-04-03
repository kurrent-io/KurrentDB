// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Data;

namespace EventStore.Core.Services.PersistentSubscription;

public interface IPersistentSubscriptionStreamPosition : IEquatable<IPersistentSubscriptionStreamPosition>, IComparable<IPersistentSubscriptionStreamPosition> {
	bool IsSingleStreamPosition { get; }
	long StreamEventNumber { get; }
	bool IsAllStreamPosition { get; }
	(long Commit, long Prepare) TFPosition { get; }
	bool IsLivePosition { get; }
	string ToString();
}
