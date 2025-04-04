// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.Data;

namespace EventStore.Core.Services.Storage.ReaderIndex;

public interface IEventFilter {
	bool IsEventAllowed(EventRecord eventRecord);
}
