// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net;
using System.Security.Cryptography.X509Certificates;
using EventStore.Plugins.Authentication;
using KurrentDB.Core;
using KurrentDB.Core.Certificates;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Services;
using KurrentDB.Core.Services.Transport.Tcp;
using Microsoft.Extensions.Hosting;

namespace KurrentDB.TcpPlugin;

public class PublicTcpApiService : IHostedService {
	public PublicTcpApiService(
		StandardComponents components,
		EventStoreOptions options,
		AuthorizationGateway authGateway,
		IAuthenticationProvider authProvider,
		CertificateProvider? certificateProvider) {

		OptionsFormatter.LogConfig("TcpPlugin", options.TcpPlugin);

		var endpoint = new IPEndPoint(options.NodeIp, options.TcpPlugin.NodeTcpPort);
		if (options.Insecure) {
			var extTcpService = new TcpService(
				components.MainQueue, endpoint, components.NetworkSendService,
				TcpServiceType.External, TcpSecurityType.Normal,
				new ClientTcpDispatcher(options.TcpPlugin.TcpReadTimeoutMs, options.WriteTimeoutMs),
				TimeSpan.FromMilliseconds(options.TcpPlugin.NodeHeartbeatInterval),
				TimeSpan.FromMilliseconds(options.TcpPlugin.NodeHeartbeatTimeout),
				authProvider,
				authGateway,
				null,
				null,
				null,
				options.ConnectionPendingSendBytesThreshold,
				options.ConnectionQueueSizeThreshold);

			components.MainBus.Subscribe<SystemMessage.SystemInit>(extTcpService);
			components.MainBus.Subscribe<SystemMessage.SystemStart>(extTcpService);
			components.MainBus.Subscribe<SystemMessage.BecomeShuttingDown>(extTcpService);
		} else {
			var extTcpSecureService = new TcpService(
				components.MainQueue, endpoint, components.NetworkSendService,
				TcpServiceType.External, TcpSecurityType.Secure,
				new ClientTcpDispatcher(options.TcpPlugin.TcpReadTimeoutMs, options.WriteTimeoutMs),
				TimeSpan.FromMilliseconds(options.TcpPlugin.NodeHeartbeatInterval),
				TimeSpan.FromMilliseconds(options.TcpPlugin.NodeHeartbeatTimeout),
				authProvider,
				authGateway,
				() => certificateProvider?.Certificate,
				() => {
					var intermediates = certificateProvider?.IntermediateCerts;
					return intermediates == null
						? null
						: new X509Certificate2Collection(intermediates);
				},
				delegate { return (true, null); },
				options.ConnectionPendingSendBytesThreshold,
				options.ConnectionQueueSizeThreshold);

			components.MainBus.Subscribe<SystemMessage.SystemInit>(extTcpSecureService);
			components.MainBus.Subscribe<SystemMessage.SystemStart>(extTcpSecureService);
			components.MainBus.Subscribe<SystemMessage.BecomeShuttingDown>(extTcpSecureService);
		}
	}

	public Task StartAsync(CancellationToken cancellationToken) {
		return Task.CompletedTask;
	}

	public Task StopAsync(CancellationToken cancellationToken) {
		return Task.CompletedTask;
	}
}
