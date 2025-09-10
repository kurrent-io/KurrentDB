// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable ArrangeTypeMemberModifiers

using Bogus;
using Grpc.Net.Client;
using KurrentDB.Testing.Fixtures;
using KurrentDB.Testing.Logging;
using KurrentDB.Core;
using KurrentDB.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Extensions.Logging;
using TUnit.Core.Interfaces;
using ILogger = Microsoft.Extensions.Logging.ILogger;

[assembly: Timeout(20_000)]

namespace KurrentDB.Api.Tests.Fixtures;

public class ApiTestFixture : ITestStartEventReceiver, ITestEndEventReceiver, ITestRetryEventReceiver {
	static ClusterVNodeApp ClusterVNodeApp { get; set; } = null!;

	protected static ClusterVNodeOptions NodeOptions  { get; private set; } = null!;
	protected static IServiceProvider    NodeServices { get; private set; } = null!;
	protected static Uri                 ServerUrl    { get; private set; } = null!;

	/// <summary>
	/// The Fixture name.
	/// </summary>
	protected string FixtureName { get; private set; } = null!;

	/// <summary>
	/// Pre-configured Faker instance for generating test data.
	/// </summary>
	protected Faker Faker => TestingManager.Faker;

	/// <summary>
	/// The logger instance associated with the test fixture.
	/// </summary>
	protected ILogger Logger { get; private set; } = null!;

	/// <summary>
	/// The logger factory instance associated with the test fixture, enabling creation of other loggers for logging purposes.
	/// </summary>
	protected ILoggerFactory LoggerFactory { get; private set; } = null!;

	/// <summary>
	/// The time provider used for simulating and controlling time in tests.
	/// </summary>
	protected FakeTimeProvider TimeProvider { get; private set; } = null!;

	/// <summary>
	/// The service provider instance configured for the test fixture.<para />
	/// This property provides access to the dependency injection container for managing
	/// services and resolving dependencies during the test lifecycle.
	/// </summary>
	protected IServiceProvider ServiceProvider => NodeServices;


	/// <summary>
	/// The gRPC channel used for communication with the test server.
	/// </summary>
	protected GrpcChannel GrpcChannel { get; set; } = null!;

	[Before(Assembly)]
	public static async Task AssemblySetUp(AssemblyHookContext context, CancellationToken cancellationToken) {
		TestingManager.AssemblySetUp();

		ClusterVNodeApp = new();

		var (options, services) = await ClusterVNodeApp.Start(
			configureServices: services => services
				.AddLogging(builder => builder
					.AddProvider(new TUnitLoggerProvider())
					.AddFilter<TUnitLoggerProvider>(null, LogLevel.Trace))
				.AddSingleton(new FakeTimeProvider(DateTimeOffset.UtcNow))
				.AddSingleton<TimeProvider>(sp => sp.GetRequiredService<FakeTimeProvider>()));

		NodeOptions  = options;
		NodeServices = services;
		ServerUrl    = new(ClusterVNodeApp.Web?.Urls.First() ?? throw new InvalidOperationException("No server URLs found!"));

		Log.Information("Test server started at {Url}", ServerUrl);
	}

	[After(Assembly)]
	public static async Task AssemblyCleanUp(AssemblyHookContext context, CancellationToken cancellationToken) {
		await ClusterVNodeApp.DisposeAsync().ConfigureAwait(false);
		await TestingManager.AssemblyCleanUp().ConfigureAwait(false);
	}

    public async ValueTask OnTestStart(TestContext testContext) {
        await TestingManager
	        .TestSetUp(testContext)
	        .ConfigureAwait(false);

        TestContext.Current!.AddAsyncLocalValues();

        FixtureName  = testContext.TestDetails.ClassType.Name;

        Logger        = ServiceProvider.GetRequiredService<ILogger<TestFixture>>();
        LoggerFactory = ServiceProvider.GetRequiredService<ILoggerFactory>();
        TimeProvider  = ServiceProvider.GetRequiredService<FakeTimeProvider>();

        GrpcChannel = GrpcChannel.ForAddress(
	        $"http://localhost:{ServerUrl.Port}",
	        new GrpcChannelOptions { LoggerFactory = LoggerFactory }
        );

    }

    public async ValueTask OnTestEnd(TestContext testContext)  {
	    GrpcChannel.Dispose();

        await TestingManager
	        .TestCleanUp(testContext)
	        .ConfigureAwait(false);
    }

    public ValueTask OnTestRetry(TestContext testContext, int retryAttempt) =>
	    TestingManager.OnTestRetry(testContext, retryAttempt);
}
