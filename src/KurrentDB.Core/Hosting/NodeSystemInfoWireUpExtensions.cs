// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable

using System;
using KurrentDB.Core.Bus;
using Microsoft.Extensions.DependencyInjection;

namespace KurrentDB.Core.Hosting;

public static class NodeSystemInfoWireUpExtensions {
	public static IServiceCollection AddNodeSystemInfoProvider(this IServiceCollection services) =>
		services.AddSingleton<GetNodeSystemInfo>(ctx => {
			var publisher = ctx.GetRequiredService<IPublisher>();
			var time      = ctx.GetRequiredService<TimeProvider>();
			return token => publisher.GetNodeSystemInfo(time, token);
		});
}
