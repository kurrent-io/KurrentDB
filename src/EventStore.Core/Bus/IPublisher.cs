// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.Messaging;

namespace EventStore.Core.Bus;

public interface IPublisher : IHandle<Message>, IEnvelope {
	void Publish(Message message);

	void IHandle<Message>.Handle(Message message) => Publish(message);

	void IEnvelope<Message>.ReplyWith<T>(T message) => Publish(message);
}
