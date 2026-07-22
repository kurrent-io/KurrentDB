// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Surge.Processors;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KurrentDB.Surge.Hosting;

public abstract class LeaderNodeProcessorWorker<T>(Func<T> getProcessor, IServiceProvider serviceProvider, string serviceName) :
	LeaderNodeBackgroundService(
		serviceProvider.GetRequiredService<IPublisher>(),
		serviceProvider.GetRequiredService<ISubscriber>(),
		serviceProvider.GetRequiredService<GetNodeSystemInfo>(),
		serviceProvider.GetRequiredService<ILoggerFactory>(),
		serviceName
	) where T : IProcessor {
	protected override async Task Execute(NodeSystemInfo nodeInfo, CancellationToken stoppingToken) {
		try {
			var processor = getProcessor();
			await processor.Activate(stoppingToken);
			await processor.Stopped;
		}
		catch (OperationCanceledException) {
			// ignored
		}
	}
}
