// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Bus;

namespace KurrentDB.Core.Authorization;

public class AuthorizationProviderFactoryComponents {
	public IPublisher MainQueue { get; set; }
	public ISubscriber MainBus { get; set; }
}
