// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.Bus;

namespace EventStore.Projections.Core.Messaging;

public class UnwrapEnvelopeHandler : IHandle<UnwrapEnvelopeMessage> {
	public void Handle(UnwrapEnvelopeMessage message) {
		message.Action();
	}
}
