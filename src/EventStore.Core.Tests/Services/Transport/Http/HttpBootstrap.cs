// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.Bus;
using EventStore.Core.Messages;
using EventStore.Core.Services.Transport.Http;
using EventStore.Core.Services.Transport.Http.Controllers;
using EventStore.Core.Tests.Fakes;

namespace EventStore.Core.Tests.Services.Transport.Http;

public class HttpBootstrap {
	public static void Subscribe(ISubscriber bus, KestrelHttpService service) {
		bus.Subscribe<SystemMessage.SystemInit>(service);
		bus.Subscribe<SystemMessage.BecomeShuttingDown>(service);
	}

	public static void Unsubscribe(ISubscriber bus, KestrelHttpService service) {
		bus.Unsubscribe<SystemMessage.SystemInit>(service);
		bus.Unsubscribe<SystemMessage.BecomeShuttingDown>(service);
	}

	public static void RegisterPing(IHttpService service) {
		service.SetupController(new PingController());
	}

	public static void RegisterStat(IHttpService service) {
		service.SetupController(new StatController(new FakePublisher(), new FakePublisher()));
	}
}
