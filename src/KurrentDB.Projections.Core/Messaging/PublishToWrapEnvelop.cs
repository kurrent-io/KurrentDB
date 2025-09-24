// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Bus;
using KurrentDB.Core.Messaging;

namespace KurrentDB.Projections.Core.Messaging;

// When this envelope is replied to it puts that message in the original envelope, however
// it arranges for this to be done elsewhere by sending a message on the provided publisher.
internal class PublishToWrapEnvelop(IPublisher publisher, IEnvelope nestedEnvelope, string extraInformation)
	: IEnvelope {
	public void ReplyWith<T>(T message) where T : Message {
		publisher.Publish(new UnwrapEnvelopeMessage(() => nestedEnvelope.ReplyWith(message), extraInformation));
	}
}
