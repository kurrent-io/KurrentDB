// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.Data;

namespace EventStore.Core.Services.Storage.ReaderIndex;

public readonly struct IndexReadEventInfoResult(EventInfo[] eventInfos, long nextEventNumber) {
	public EventInfo[] EventInfos { get; } = eventInfos;
	public long NextEventNumber { get; } = nextEventNumber;
	public bool IsEndOfStream => NextEventNumber < 0;
}
