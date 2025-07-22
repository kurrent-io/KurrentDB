// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using EventStore.Core.Bus;
using EventStore.Core.Messaging;

namespace EventStore.Projections.Core.Messaging;

// When this envelope is replied to it puts that message in the original envelope, however
// it arranges for this to be done elsewhere by sending a message on the provided publisher.
class PublishToWrapEnvelop : IEnvelope {
	private readonly IPublisher _publisher;
	private readonly IEnvelope _nestedEnvelope;
	private readonly string _extraInformation;

	public PublishToWrapEnvelop(IPublisher publisher, IEnvelope nestedEnvelope, string extraInformation) {
		_publisher = publisher;
		_nestedEnvelope = nestedEnvelope;
		_extraInformation = extraInformation;
	}

	public void ReplyWith<T>(T message) where T : Message {
		_publisher.Publish(new UnwrapEnvelopeMessage(() => _nestedEnvelope.ReplyWith(message), _extraInformation));
	}
}
