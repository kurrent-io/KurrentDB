// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.Bus;
using EventStore.Core.Messaging;

namespace EventStore.Core.XUnit.Tests;

class EnvelopePublisher : IPublisher {
	private readonly IEnvelope _envelope;

	public EnvelopePublisher(IEnvelope envelope) {
		_envelope = envelope;
	}

	public void Publish(Message message) {
		_envelope.ReplyWith(message);
	}
}
