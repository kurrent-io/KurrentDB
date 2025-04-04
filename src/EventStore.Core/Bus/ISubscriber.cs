// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.Messaging;

namespace EventStore.Core.Bus;

public interface ISubscriber {
	void Subscribe<T>(IAsyncHandle<T> handler) where T : Message;
	void Unsubscribe<T>(IAsyncHandle<T> handler) where T : Message;
}
