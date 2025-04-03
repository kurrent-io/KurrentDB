// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.Bus;
using EventStore.Core.Services;
using EventStore.Core.Services.Transport.Http;

namespace EventStore.Core.Authentication;

public record AuthenticationProviderFactoryComponents {
	public IPublisher MainQueue { get; init; }
	public ISubscriber MainBus { get; init; }
	public IPublisher WorkersQueue { get; init; }
	public InMemoryBus[] WorkerBuses { get; init; }
	public HttpSendService HttpSendService { get; init; }
	public IHttpService HttpService { get; init; }
}
