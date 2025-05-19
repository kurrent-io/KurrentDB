using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Model.Compose;
using Ductus.FluentDocker.Model.Containers;
using Ductus.FluentDocker.Services;
using Ductus.FluentDocker.Services.Impl;

namespace KurrentDB.Surge.Testing.Containers.FluentDocker;

[PublicAPI]
public static class FluentDockerServiceExtensions {
	static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromMilliseconds(100);

    public static DockerComposeConfig Configuration(this ICompositeService service) =>
        (DockerComposeConfig)typeof(DockerComposeCompositeService)
            .GetProperty("Config", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(service)!;

	public static async Task WaitUntilNodesAreHealthy(this IContainerService service, CancellationToken cancellationToken) {
		while (true) {
			var config = service.GetConfiguration(true);
			var status = config?.State?.Health?.Status;

			if (status is HealthState.Healthy) return;

			if (cancellationToken.IsCancellationRequested)
				throw new FluentDockerException($"Wait for healthy expired for container {service.Id}");

			// ReSharper disable once MethodSupportsCancellation
			await Task.Delay(DefaultRetryDelay);
		}
	}

	public static async ValueTask WaitUntilNodesAreHealthy(this IContainerService service, TimeSpan timeout) {
		using var cts = new CancellationTokenSource(timeout);
		await WaitUntilNodesAreHealthy(service, cts.Token);
	}

	public static async Task WaitUntilNodesAreHealthy(this ICompositeService service, IEnumerable<string> services, CancellationToken cancellationToken) {
		var nodes = service.Containers.Where(x => services.Contains(x.Name));

		await Parallel.ForEachAsync(nodes, cancellationToken, async (node, ct) => await node.WaitUntilNodesAreHealthy(ct));
	}

	public static async Task WaitUntilNodesAreHealthy(this ICompositeService service, string serviceNamePrefix, CancellationToken cancellationToken) {
		var nodes = service.Containers.Where(x => x.Name.StartsWith(serviceNamePrefix));
		await Parallel.ForEachAsync(nodes, cancellationToken, async (node, ct) => await node.WaitUntilNodesAreHealthy(ct));
	}

	public static async Task WaitUntilNodesAreHealthy(this ICompositeService service, string serviceNamePrefix, TimeSpan timeout) {
		using var cts = new CancellationTokenSource(timeout);
		await WaitUntilNodesAreHealthy(service, serviceNamePrefix, cts.Token);
	}
}
