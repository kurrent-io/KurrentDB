// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace KurrentDB.Core.Hosting;

public abstract class SystemBackgroundService(SystemReadinessProbe probe) : BackgroundService {
	SystemReadinessProbe ReadinessProbe { get; } = probe;

	protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
		var nodeInfo = await ReadinessProbe.WaitUntilReady(stoppingToken);
		await Execute(nodeInfo, stoppingToken);
	}

	protected abstract Task Execute(NodeSystemInfo nodeInfo, CancellationToken stoppingToken);
}
