// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace KurrentDB.Core;

// Implementations of this interface are run to completion during startup
public interface IClusterVNodeStartupTask {
	ValueTask Run(CancellationToken token);
}

public static class ServiceCollectionExtensions {
	public static void AddStartupTask(this IServiceCollection serviceCollection, Func<IServiceProvider, IClusterVNodeStartupTask> startupTaskFactory) {
		serviceCollection.Decorate<IReadOnlyList<IClusterVNodeStartupTask>>((startupTasks, serviceProvider) => {
			var newStartupTasks = new List<IClusterVNodeStartupTask>(startupTasks ?? []) {
				startupTaskFactory(serviceProvider),
			};
			return newStartupTasks;
		});
	}
}
