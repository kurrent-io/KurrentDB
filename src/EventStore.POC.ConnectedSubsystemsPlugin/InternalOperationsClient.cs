// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.Bus;
using EventStore.Core.Messages;
using EventStore.POC.IO.Core;

namespace EventStore.POC.ConnectedSubsystemsPlugin;

public class InternalOperationsClient : IOperationsClient {
	readonly IPublisher _publisher;

	public InternalOperationsClient(IPublisher publisher) {
		_publisher = publisher;
	}

	public void Resign() {
		_publisher.Publish(new ClientMessage.ResignNode());
	}
}
