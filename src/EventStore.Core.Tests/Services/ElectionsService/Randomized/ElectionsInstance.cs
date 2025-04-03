// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Net;
using EventStore.Core.Bus;

namespace EventStore.Core.Tests.Services.ElectionsService.Randomized;

internal class ElectionsInstance {
	public readonly Guid InstanceId;
	public readonly EndPoint EndPoint;

	public readonly IPublisher InputBus;
	public readonly IPublisher OutputBus;

	public ElectionsInstance(Guid instanceId, EndPoint endPoint, IPublisher inputBus, IPublisher outputBus) {
		InstanceId = instanceId;
		EndPoint = endPoint;
		InputBus = inputBus;
		OutputBus = outputBus;
	}
}
