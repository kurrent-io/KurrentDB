// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading.Tasks;
using Ductus.FluentDocker.Extensions;
using Ductus.FluentDocker.Services;
using Ductus.FluentDocker.Services.Extensions;
using Xunit.Abstractions;

namespace EventStore.Auth.OAuth.Tests;

internal static class ContainerExtensions {
	public static void ShipContainerLogs(this IContainerService container, ITestOutputHelper testOutputHelper) =>
		Task.Run(() => {
			using var logs = container.Logs(true);
			foreach (var line in logs.ReadToEnd()) {
				testOutputHelper.WriteLine(line);
			}
		});
}
