// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace KurrentDB.Components.Tests;

// The host only watches ExecuteTask (and applies BackgroundServiceExceptionBehavior) for services that are
// BackgroundService instances, so the lifecycle decorator must not hide that from it.
public class HostedServiceLifecycleDecoratorTests {
	class PlainService : IHostedService {
		public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
		public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
	}

	class WaitingService : BackgroundService {
		protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
			Task.Delay(Timeout.Infinite, stoppingToken);
	}

	class FailingService : BackgroundService {
		protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
			await Task.Yield();
			throw new InvalidOperationException("failed after startup");
		}
	}

	[Fact]
	public void Background_services_stay_background_services() {
		Assert.IsType<BackgroundServiceLifecycleDecorator>(HostedServiceLifecycleDecorator.Decorate(new WaitingService()));
		Assert.IsType<HostedServiceLifecycleDecorator>(HostedServiceLifecycleDecorator.Decorate(new PlainService()));
	}

	[Fact]
	public async Task Execute_task_is_the_inner_services_task() {
		var inner = new WaitingService();
		var decorator = (BackgroundService)HostedServiceLifecycleDecorator.Decorate(inner);

		await decorator.StartAsync(TestContext.Current.CancellationToken);
		Assert.NotNull(inner.ExecuteTask);
		Assert.Same(inner.ExecuteTask, decorator.ExecuteTask);

		await decorator.StopAsync(TestContext.Current.CancellationToken);
		Assert.True(inner.ExecuteTask.IsCompleted);
	}

	[Fact]
	public async Task Stop_host_applies_to_a_decorated_background_service() {
		var builder = Host.CreateEmptyApplicationBuilder(new());
		builder.Services.Configure<HostOptions>(x =>
			x.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.StopHost);
		builder.Services.AddSingleton<IHostedService>(_ => HostedServiceLifecycleDecorator.Decorate(new FailingService()));
		using var host = builder.Build();

		var stopping = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping
			.Register(() => stopping.TrySetResult());

		await host.StartAsync(TestContext.Current.CancellationToken);
		// Behind a decorator that hides BackgroundService, the failure goes unnoticed and this times out.
		await stopping.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
	}
}
