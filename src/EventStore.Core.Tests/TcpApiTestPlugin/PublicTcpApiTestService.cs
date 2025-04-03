// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable

using System;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core;
using EventStore.Core.Bus;
using EventStore.Core.Certificates;
using EventStore.Core.Messages;
using EventStore.Core.Services;
using EventStore.Core.Services.Transport.Tcp;
using EventStore.Plugins.Authentication;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace EventStore.TcpUnitTestPlugin;

public class PublicTcpApiTestService : IHostedService {
	static readonly ILogger Logger = Log.ForContext<PublicTcpApiTestService>();

	PublicTcpApiTestService(TcpService tcpService, ISubscriber bus) {
		bus.Subscribe<SystemMessage.SystemInit>(tcpService);
		bus.Subscribe<SystemMessage.SystemStart>(tcpService);
		bus.Subscribe<SystemMessage.BecomeShuttingDown>(tcpService);

		_ = Task.Run(async () => {
			await Task.Delay(TimeSpan.FromHours(1));
			Logger.Warning("Shutting down TCP unit tests");
			tcpService.Handle(new SystemMessage.BecomeShuttingDown(Guid.NewGuid(), true, true));
		});
	}

	public static PublicTcpApiTestService Insecure(
		TcpApiTestOptions options, 
		IAuthenticationProvider authProvider,
		AuthorizationGateway authGateway, 
		StandardComponents components
	) {
		var endpoint = new IPEndPoint(IPAddress.Loopback, options.NodeTcpPort);

		var tcpService = new TcpService(
			publisher: components.MainQueue,
			serverEndPoint: endpoint,
			networkSendQueue: components.NetworkSendService,
			serviceType: TcpServiceType.External, securityType: TcpSecurityType.Normal,
			dispatcher: new ClientTcpDispatcher(options.WriteTimeoutMs),
			heartbeatInterval: TimeSpan.FromMilliseconds(options.NodeHeartbeatInterval),
			heartbeatTimeout: TimeSpan.FromMilliseconds(options.NodeHeartbeatTimeout),
			authProvider: authProvider,
			authorizationGateway: authGateway,
			certificateSelector: null,
			intermediatesSelector: null,
			sslClientCertValidator: null,
			connectionPendingSendBytesThreshold: options.ConnectionPendingSendBytesThreshold,
			connectionQueueSizeThreshold: options.ConnectionQueueSizeThreshold
		);

		return new(tcpService, components.MainBus);
	}

	public static PublicTcpApiTestService Secure(
		TcpApiTestOptions options, 
		IAuthenticationProvider authProvider, 
		AuthorizationGateway authGateway, 
		StandardComponents components, 
		CertificateProvider? certificateProvider
	) {
		var endpoint = new IPEndPoint(IPAddress.Loopback, options.NodeTcpPort);

		var tcpService = new TcpService(
			publisher: components.MainQueue,
			serverEndPoint: endpoint,
			networkSendQueue: components.NetworkSendService,
			serviceType: TcpServiceType.External, securityType: TcpSecurityType.Secure,
			dispatcher: new ClientTcpDispatcher(options.WriteTimeoutMs),
			heartbeatInterval: TimeSpan.FromMilliseconds(options.NodeHeartbeatInterval),
			heartbeatTimeout: TimeSpan.FromMilliseconds(options.NodeHeartbeatTimeout),
			authProvider: authProvider,
			authorizationGateway: authGateway,
			certificateSelector: () => certificateProvider?.Certificate,
			intermediatesSelector: () => {
				var intermediates = certificateProvider?.IntermediateCerts;
				return intermediates == null ? null : new X509Certificate2Collection(intermediates);
			},
			sslClientCertValidator: delegate { return (true, null); },
			connectionPendingSendBytesThreshold: options.ConnectionPendingSendBytesThreshold,
			connectionQueueSizeThreshold: options.ConnectionQueueSizeThreshold
		);

		return new(tcpService, components.MainBus);
	}

	public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

	public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
