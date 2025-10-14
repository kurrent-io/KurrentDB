// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Bus;
using KurrentDB.Core.Services;
using KurrentDB.Core.Services.Transport.Http;

namespace KurrentDB.Core.Authentication;

public record AuthenticationProviderFactoryComponents {
	public IPublisher MainQueue { get; init; }
	public ISubscriber MainBus { get; init; }
	public IPublisher WorkersQueue { get; init; }
	public InMemoryBus[] WorkerBuses { get; init; }
	public HttpSendService HttpSendService { get; init; }
	public IHttpService HttpService { get; init; }
}
