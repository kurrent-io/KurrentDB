// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.Messages;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Services.Transport.Http;
using KurrentDB.Core.Services.Transport.Http.Controllers;
using KurrentDB.Core.Tests.Fakes;

namespace KurrentDB.Core.Tests.Services.Transport.Http;

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
