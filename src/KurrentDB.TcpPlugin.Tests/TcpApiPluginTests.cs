// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using DotNext.Net.Http;
using EventStore.ClientAPI;
using EventStore.Plugins;
using EventStore.Plugins.Authentication;
using EventStore.Plugins.Licensing;
using KurrentDB.Core;
using KurrentDB.Core.Authentication;
using KurrentDB.Core.Authentication.DelegatedAuthentication;
using KurrentDB.Core.Authentication.PassthroughAuthentication;
using KurrentDB.Core.Authorization;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Certificates;
using KurrentDB.Core.Configuration.Sources;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Metrics;
using KurrentDB.Core.Services;
using KurrentDB.Core.Services.TimerService;
using KurrentDB.Core.Services.Transport.Http;
using KurrentDB.Core.Tests.Helpers;
using KurrentDB.Core.Tests.TransactionLog;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KurrentDB.TcpPlugin.Tests;

public class TcpApiPluginTests {
	private const string LicenseToken = "eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9.eyJhdWQiOiJlc2RiIiwiaXNzIjoiZXNkYiIsImV4cCI6MTgyNDQ1NjgyOSwianRpIjoiOTVmZTY2YzAtMDRkMi00MjExLWI1ZGQtNTAyM2MyYTAxMGFiIiwic3ViIjoiRVNEQiBUZXN0cyIsIklzVHJpYWwiOiJUcnVlIiwiSXNFeHBpcmVkIjoiRmFsc2UiLCJJc1ZhbGlkIjoiVHJ1ZSIsIklzRmxvYXRpbmciOiJUcnVlIiwiRGF5c1JlbWFpbmluZyI6IjEiLCJTdGFydERhdGUiOiIyNi8wNC8yMDI0IDAwOjAwOjAwICswMTowMCIsIk5PTkUiOiJ0cnVlIiwiaWF0IjoxNzI5ODQ4ODI5LCJuYmYiOjE3Mjk4NDg4Mjl9.R24i-ZAow3BhRaST3n25Uc_nQ184k83YRZZ0oRcWbU9B9XNLRH0Iegj0HmkyzkT50I4gcIJOIfcO6mIPp4Y959CP7aTAlt7XEnXoGF0GwsfXatAxy4iXG8Gpya7INgMoWEeN0v8eDH8_OVmnieOxeba9ex5j1oAW_FtQDMzcFjAeErpW__8zmkCsn6GzvlhdLE4e3r2wjshvrTTcS_1fvSVjQZov5ce2sVBJPegjCLO_QGiIBK9QTnpHrhe6KCYje6fSTjgty0V1Qj22bftvrXreYzQijPrnC_ek1BwV-A1JvacZugMCPIy8WvE5jE3hVYRWGGUzQZ-CibPGsjudYA";
	private readonly TcpApiPlugin _sut;
	private readonly StandardComponents _components;
	private readonly int _port;
	private readonly WebApplicationBuilder _builder;
	private readonly WebApplication _app;
	private readonly TcpMessageCollector _collector;

	public TcpApiPluginTests() {
		_collector = new TcpMessageCollector();
		_builder = WebApplication.CreateBuilder();
		_sut = new TcpApiPlugin();
		_port = PortsHelper.GetAvailablePort(IPAddress.Loopback);
		var httpPort = PortsHelper.GetAvailablePort(IPAddress.Loopback);

		_builder.Configuration.AddInMemoryCollection([
			new($"{KurrentConfigurationKeys.Prefix}:Insecure", "true"),
			new($"{KurrentConfigurationKeys.Prefix}:TcpPlugin:NodeTcpPort", _port.ToString()),
			new($"{KurrentConfigurationKeys.Prefix}:TcpPlugin:EnableExternalTcp", "true")
		]);
		var workerBus = new InMemoryBus("Worker Bus", _ => TimeSpan.Zero);

		_components = CreateStandardComponents(workerBus);
		var httpPipe = new HttpMessagePipe();
		var httpSendService = new HttpSendService(httpPipe, true, delegate { return (true, ""); });
		var httpService = new KestrelHttpService(ServiceAccessibility.Public, _components.MainQueue, new TrieUriRouter(),
			false,
			"localhost",
			_port,
			new HttpEndPoint(IPAddress.Loopback, httpPort, false));

		var components = new AuthenticationProviderFactoryComponents {
			MainBus = _components.MainBus,
			MainQueue = _components.MainQueue,
			WorkerBus = workerBus,
			WorkersQueue = _components.NetworkSendService,
			HttpSendService = httpSendService,
			HttpService = httpService,
		};

		var authorizationProviderFactory =
			new AuthorizationProviderFactory(_ => new PassthroughAuthorizationProviderFactory());
		var authenticationProviderFactory =
			new AuthenticationProviderFactory(_ => new PassthroughAuthenticationProviderFactory());

		var authenticationProvider = new DelegatedAuthenticationProvider(
			authenticationProviderFactory.GetFactory(components).Build(false));

		var authorizationProvider = authorizationProviderFactory.GetFactory(
			new AuthorizationProviderFactoryComponents {
				MainQueue = _components.MainQueue,
				MainBus = _components.MainBus
			}).Build();
		var authGateway = new AuthorizationGateway(authorizationProvider);
		var licenseService = new FakeLicenseService(LicenseToken);

		_components.MainBus.Subscribe(_collector);

		_builder.Services.AddSingleton(_components);
		_builder.Services.AddSingleton<IAuthenticationProvider>(authenticationProvider);
		_builder.Services.AddSingleton(authorizationProvider);
		_builder.Services.AddSingleton(authGateway);
		_builder.Services.AddSingleton<CertificateProvider>(_ => null!);
		_builder.Services.AddSingleton<ILicenseService>(licenseService);

		((IPlugableComponent)_sut).ConfigureServices(_builder.Services, _builder.Configuration);
		_app = _builder.Build();
		((IPlugableComponent)_sut).ConfigureApplication(_app, _builder.Configuration);
		_app.StartAsync();
		_components.MainQueue.Publish(new SystemMessage.SystemInit());
	}

	private static StandardComponents CreateStandardComponents(InMemoryBus workerBus) {
		var queueStatsManager = new QueueStatsManager();
		var queueTrackers = new QueueTrackers();
		var workersHandler = new ThreadPoolMessageScheduler("Worker Scheduler", workerBus) {
			SynchronizeMessagesWithUnknownAffinity = false,
		};

		var dbConfig = TFChunkHelper.CreateDbConfig(Path.GetTempPath(), 0);
		var mainBus = new InMemoryBus("mainBus", _ => TimeSpan.Zero);
		var mainQueue = new ThreadPoolMessageScheduler("MainQueue", mainBus) {
			SynchronizeMessagesWithUnknownAffinity = true,
		};
		mainQueue.Start();
		var threadBasedScheduler = new ThreadBasedScheduler(queueStatsManager, queueTrackers);
		var timerService = new TimerService(threadBasedScheduler);

		return new StandardComponents(dbConfig, mainQueue, mainBus,
			timerService, timeProvider: null, httpForwarder: null, httpServices: [],
			networkSendService: workersHandler, queueStatsManager: queueStatsManager,
			trackers: queueTrackers,
			metricsConfiguration: new());
	}

	[Fact]
	public async Task can_receive_tcp_connection() {
		var connection = EventStoreConnection.Create(
			$"ConnectTo=tcp://admin:changeit@localhost:{_port}; UseSslConnection=false"
		);

		await connection.ConnectAsync();
		_ = Task.Run(() => connection.ReadEventAsync("foobar", 42, true));

		var msg = await _collector.Message.WaitAsync(TimeSpan.FromSeconds(30));
		Assert.Equal("foobar", msg.EventStreamId);
		Assert.Equal(42, msg.EventNumber);
		Assert.True(msg.ResolveLinkTos);

		_components.MainQueue.Publish(new SystemMessage.BecomeShuttingDown(Guid.NewGuid(), true, true));
	}
}
