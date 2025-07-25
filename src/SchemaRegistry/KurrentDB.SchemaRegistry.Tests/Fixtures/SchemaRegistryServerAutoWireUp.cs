// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable ArrangeTypeMemberModifiers

using System.Net;
using Grpc.Net.Client;
using Kurrent.Surge.Testing.TUnit.Logging;
using KurrentDB.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using static KurrentDB.Protocol.Registry.V2.SchemaRegistryService;

namespace KurrentDB.SchemaRegistry.Tests.Fixtures;

public class SchemaRegistryServerAutoWireUp {
	public static SchemaRegistryServiceClient Client          { get; private set; } = null!;
	public static ClusterVNodeOptions         NodeOptions     { get; private set; } = null!;
	public static IServiceProvider            NodeServices    { get; private set; } = null!;

	static ClusterVNodeApp ClusterVNodeApp { get; set; } = null!;
	static GrpcChannel     GrpcChannel     { get; set; } = null!;
	static HttpClient      HttpClient      { get; set; } = null!;

	[Before(Assembly)]
	public static async Task AssemblySetUp(AssemblyHookContext context, CancellationToken cancellationToken) {
		ClusterVNodeApp = new ClusterVNodeApp();

		var (options, services) = await ClusterVNodeApp.Start(configureServices: ConfigureServices);

		var serverUrl = new Uri(ClusterVNodeApp.ServerUrls.First());

		NodeServices = services;
		NodeOptions = options;

		Serilog.Log.Information("Test server started at {Url}", serverUrl);

		HttpClient = new HttpClient {
			BaseAddress = serverUrl,
			DefaultRequestVersion = HttpVersion.Version20,
			DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact
		};

		GrpcChannel = GrpcChannel.ForAddress(
			$"http://localhost:{serverUrl.Port}",
			new GrpcChannelOptions {
				HttpClient = HttpClient,
				LoggerFactory = services.GetRequiredService<ILoggerFactory>()
			}
		);

		Client = new SchemaRegistryServiceClient(GrpcChannel);
	}

	static void ConfigureServices(IServiceCollection services) {
		var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);

        services.AddSingleton<ILoggerFactory>(new TestContextAwareLoggerFactory());
		services.AddSingleton<TimeProvider>(timeProvider);
		services.AddSingleton(timeProvider);
	}

	[After(Assembly)]
	public static async Task AssemblyCleanUp() {
		await ClusterVNodeApp.DisposeAsync();
		HttpClient.Dispose();
		GrpcChannel.Dispose();
	}

	private class TestContextAwareLoggerFactory : ILoggerFactory {
		readonly ILoggerFactory _defaultLoggerFactory = new LoggerFactory();

		public ILogger CreateLogger(string categoryName) {
			return TestContext.Current is not null
				? TestContext.Current.LoggerFactory().CreateLogger(categoryName)
				: _defaultLoggerFactory.CreateLogger(categoryName);
		}

		public void AddProvider(ILoggerProvider provider) {
			throw new NotImplementedException();
		}

		public void Dispose() {
			_defaultLoggerFactory.Dispose();
		}
	}
}
