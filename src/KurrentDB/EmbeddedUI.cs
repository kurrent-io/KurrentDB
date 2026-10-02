// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Linq;
using KurrentDB.Components;
using KurrentDB.Components.Cluster;
using KurrentDB.Components.Dashboard;
using KurrentDB.Components.PersistentSubscriptions;
using KurrentDB.Components.Plugins;
using KurrentDB.Components.Projections;
using KurrentDB.Components.Scavenges;
using KurrentDB.Components.ServerInfo;
using KurrentDB.Components.Shared;
using KurrentDB.Components.Stats;
using KurrentDB.Components.Streams;
using KurrentDB.Components.Users;
using KurrentDB.Core;
using KurrentDB.Services;
using KurrentDB.Tools;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MudBlazor;
using MudBlazor.Services;

namespace KurrentDB;

/// <summary>
/// Registrations methods for the embedded Blazor embedded UI.
/// </summary>
public static class EmbeddedUI {
	public static void ConfigureServices(IServiceCollection services) {
		// Scoped, not singleton: theme is a per-user (per-circuit) preference, not server-global.
		// HttpContextAccessor lets Preferences read the theme cookie server-side to seed flicker-free.
		services.AddHttpContextAccessor();
		// Page authorization: map [Authorize(Policy = UiPolicies.X)] to a KurrentDB Operation check.
		services.AddAuthorization(UiPolicies.Configure);
		services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, OperationAuthorizationHandler>();
		services.AddScoped<UI.Services.Preferences>();
		services
			.AddRazorComponents()
			.AddInteractiveServerComponents();
		services.AddCascadingAuthenticationState();
		services.AddMudServices(config => {
			config.SnackbarConfiguration.PositionClass = MudBlazor.Defaults.Classes.Position.BottomRight;
		});
		services.AddMudMarkdownServices();
		services.AddScoped<LogObserver>();
		services.AddScoped<ClipboardService>();
		services.AddSingleton(new MonitoringService());
		services.AddSingleton(new MetricsObserver());
		services.AddSingleton<PluginsService>();
		services.AddScoped<UserManagementService>();
		services.AddScoped<ClusterOperationsService>();
		// Optional resolution: IKontroller is only registered on a Kontrol Plane node.
		services.AddScoped(sp => new KontrolPlaneService(sp.GetService<KontrolPlane.IKontroller>()));
		// Process-wide node-role tracker (subscribes to $mem-node-state); shared by all UI circuits.
		services.AddSingleton<GossipMonitor>();
		services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<GossipMonitor>());
		services.AddScoped<ScavengeService>();
		services.AddScoped<DashboardService>();
		services.AddScoped<StreamsService>();
		services.AddScoped(sp => {
			// ProjectionsService publishes to the projections subsystem's leader input queue. When projections
			// are disabled on this node (e.g. --run-projections=None) the subsystem is absent; resolve a service
			// in the "unavailable" state (null queue) rather than throwing, so injecting it into the Projections
			// page doesn't crash the circuit — the page shows a calm "not enabled" message instead.
			var opts = sp.GetRequiredService<ClusterVNodeOptions>();
			var projectionsPublisher = opts.Subsystems.OfType<Projections.Core.ProjectionsSubsystem>().FirstOrDefault()?.LeaderInputQueue;
			return new ProjectionsService(projectionsPublisher, sp.GetRequiredService<EventStore.Plugins.Authorization.IAuthorizationProvider>());
		});
		services.AddScoped<PersistentSubscriptionsService>();
		services.AddScoped<ServerInfoService>();
		services.AddScoped(sp => {
			// Register via a factory (like ProjectionsService above) rather than by type:
			// ValidateOnBuild constructs every type-registered descriptor up front and
			// would fail resolving StatsService when secondary indexing is disabled.
			return new UiStatsService(
				sp.GetRequiredService<SecondaryIndexing.Stats.StatsService>(),
				sp.GetRequiredService<EventStore.Plugins.Authorization.IAuthorizationProvider>());
		});
	}

	/// <summary>
	/// Must run ahead of <c>Startup.Configure</c>, which sets up routing and the endpoints: a Blazor circuit
	/// request would otherwise be handled by its endpoint before reaching this middleware.
	/// </summary>
	public static void UseShutdownGuard(WebApplication app) =>
		app.UseMiddleware<BlazorShutdownMiddleware>();

	public static void Configure(WebApplication app) {
		app.MapStaticAssets();
		app.MapRazorComponents<App>()
			.DisableAntiforgery()
			.AddInteractiveServerRenderMode();
	}
}
