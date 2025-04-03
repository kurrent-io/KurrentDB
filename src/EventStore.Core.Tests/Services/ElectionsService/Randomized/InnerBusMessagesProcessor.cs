// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Net;
using EventStore.Core.Bus;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Services.TimerService;
using EventStore.Core.Tests.Infrastructure;

namespace EventStore.Core.Tests.Services.ElectionsService.Randomized;

internal class InnerBusMessagesProcessor : IHandle<Message> {
	private readonly RandomTestRunner _runner;
	private readonly IPEndPoint _endPoint;
	private readonly IPublisher _bus;

	public InnerBusMessagesProcessor(RandomTestRunner runner, IPEndPoint endPoint, IPublisher bus) {
		if (runner == null) throw new ArgumentNullException("runner");
		if (endPoint == null) throw new ArgumentNullException("endPoint");
		if (bus == null) throw new ArgumentNullException("bus");

		_runner = runner;
		_endPoint = endPoint;
		_bus = bus;
	}

	public void Handle(Message message) {
		// timer message and SendOverGrpc is handled differently
		if (message is TimerMessage.Schedule)
			return;

		_runner.Enqueue(_endPoint, message, _bus); // process with no delay and no reorderings
	}
}
