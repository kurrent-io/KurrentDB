// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.Bus;
using EventStore.Core.Messaging;

namespace EventStore.Projections.Core.Messaging;

class PublishToWrapEnvelop : IEnvelope {
	private readonly IPublisher _publisher;
	private readonly IEnvelope _nestedEnevelop;

	public PublishToWrapEnvelop(IPublisher publisher, IEnvelope nestedEnevelop) {
		_publisher = publisher;
		_nestedEnevelop = nestedEnevelop;
	}

	public void ReplyWith<T>(T message) where T : Message {
		_publisher.Publish(new UnwrapEnvelopeMessage(() => _nestedEnevelop.ReplyWith(message)));
	}
}
