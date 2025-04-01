// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using EventStore.Common.Configuration;
using EventStore.Common.Utils;
using EventStore.Core.Bus;
using EventStore.Core.Messages;
using EventStore.Core.Metrics;
using EventStore.Core.Services.Storage.ReaderIndex;
using EventStore.Core.Services.Transport.Grpc;
using EventStore.Core.Services.Transport.Grpc.Cluster;
using EventStore.Core.Services.Transport.Http;
using EventStore.Core.TransactionLog.Chunks;
using EventStore.Plugins;
using EventStore.Plugins.Authentication;
using EventStore.Plugins.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using MidFunc = System.Func<
	Microsoft.AspNetCore.Http.HttpContext,
	System.Func<System.Threading.Tasks.Task>,
	System.Threading.Tasks.Task
>;
using Operations = EventStore.Core.Services.Transport.Grpc.Operations;
using ClusterGossip = EventStore.Core.Services.Transport.Grpc.Cluster.Gossip;
using ClientGossip = EventStore.Core.Services.Transport.Grpc.Gossip;
using ServerFeatures = EventStore.Core.Services.Transport.Grpc.ServerFeatures;
using Serilog;
using AuthenticationMiddleware = EventStore.Core.Services.Transport.Http.AuthenticationMiddleware;
using HttpMethod = EventStore.Transport.Http.HttpMethod;

#nullable enable
namespace EventStore.Core;

public class ClusterVNodeStartup<TStreamId> : IInternalStartup, IHandle<SystemMessage.SystemReady>,
	IHandle<SystemMessage.BecomeShuttingDown> {
	readonly ClusterVNodeOptions _options;
	private readonly IReadOnlyList<IPlugableComponent> _plugableComponents;
	private readonly IPublisher _mainQueue;
	private readonly IPublisher _monitoringQueue;
	private readonly ISubscriber _mainBus;
	private readonly IAuthenticationProvider _authenticationProvider;
	private readonly IExpiryStrategy _expiryStrategy;
	private readonly KestrelHttpService _httpService;
	private readonly IConfiguration _configuration;
	private readonly MetricsConfiguration _metricsConfiguration;
	private readonly Trackers _trackers;
	private readonly StatusCheck _statusCheck;
	private readonly Func<IServiceCollection, IServiceCollection> _configureNodeServices;
	private readonly Action<IApplicationBuilder> _configureNode;

	private bool _ready;
	private readonly IAuthorizationProvider _authorizationProvider;
	private readonly MultiQueuedHandler _httpMessageHandler;
	private readonly string? _clusterDns;

	public ClusterVNodeStartup(
		ClusterVNodeOptions options,
		IReadOnlyList<IPlugableComponent> plugableComponents,
		IPublisher mainQueue,
		IPublisher monitoringQueue,
		ISubscriber mainBus,
		MultiQueuedHandler httpMessageHandler,
		IAuthenticationProvider authenticationProvider,
		IAuthorizationProvider authorizationProvider,
		IExpiryStrategy expiryStrategy,
		KestrelHttpService httpService,
		IConfiguration configuration,
		Trackers trackers,
		Func<IServiceCollection, IServiceCollection> configureNodeServices,
		Action<IApplicationBuilder> configureNode) {
		_options = options;
		_plugableComponents = plugableComponents;
		_mainQueue = mainQueue;
		_monitoringQueue = monitoringQueue ?? throw new ArgumentNullException(nameof(monitoringQueue));
		_mainBus = mainBus ?? throw new ArgumentNullException(nameof(mainBus));
		_httpMessageHandler = httpMessageHandler;
		_authenticationProvider = authenticationProvider;
		_authorizationProvider = authorizationProvider ?? throw new ArgumentNullException(nameof(authorizationProvider));
		_expiryStrategy = expiryStrategy;
		_httpService = Ensure.NotNull(httpService);
		_configuration = Ensure.NotNull(configuration);
		_metricsConfiguration = MetricsConfiguration.Get(_configuration);
		_trackers = trackers;
		_clusterDns = options.Cluster.DiscoverViaDns ? options.Cluster.ClusterDns : null;
		_configureNodeServices = configureNodeServices ?? throw new ArgumentNullException(nameof(configureNodeServices));
		_configureNode = configureNode ?? throw new ArgumentNullException(nameof(configureNode));
		_statusCheck = new StatusCheck(this);
	}

	public void Configure(WebApplication app) {
		_configureNode(app);

		var forcePlainTextMetrics =
			_metricsConfiguration.LegacyCoreNaming &&
			_metricsConfiguration.LegacyProjectionsNaming;

		var internalDispatcher = new InternalDispatcherEndpoint(_mainQueue, _httpMessageHandler);
		_mainBus.Subscribe(internalDispatcher);

		app.Map("/health", _statusCheck.Configure);
		app.UseCors("default");

		// AuthenticationMiddleware uses _httpAuthenticationProviders and assigns
		// the resulting ClaimsPrinciple to HttpContext.User
		app.UseMiddleware<AuthenticationMiddleware>();

		// UseAuthentication/UseAuthorization allow the rest of the pipeline to access auth
		// in a conventional way (e.g. with AuthorizeAttribute). The server doesn't make use
		// of this yet but plugins may. The registered authentication scheme (kurrent auth)
		// is driven by the HttpContext.User established above
		app.UseAuthentication();
		app.UseRouting();
		app.UseAuthorization();
		app.UseAntiforgery();

		// allow all subsystems to register their legacy controllers before calling MapLegacyHttp
		foreach (var component in _plugableComponents)
			component.ConfigureApplication(app, _configuration);

		_authenticationProvider.ConfigureEndpoints(app);
		app.UseStaticFiles();

		// Select an appropriate controller action and codec.
		//    Success -> Add InternalContext (HttpEntityManager, urimatch, ...) to HttpContext
		//    Fail -> Pipeline terminated with response.
		app.UseMiddleware<KestrelToInternalBridgeMiddleware>();

		// Looks up the InternalContext to perform the check.
		// Terminal if auth check is not successful.
		app.UseMiddleware<AuthorizationMiddleware>();

		// Open telemetry currently guarded by our custom authz for consistency with stats
		app.UseOpenTelemetryPrometheusScrapingEndpoint(x => {
			if (x.Request.Path != "/metrics")
				return false;

			// Prometheus scrapes preferring application/openmetrics-text, but the prometheus exporter
			// these days adds `_total` suffix to counters when outputting openmetrics format (as
			// required by the spec). DisableTotalNameSuffixForCounters only affects plain text output.
			// So if we are exporting legacy metrics, where we do not want the _total suffix for
			// backwards compatibility, then force the exporter to respond with plain text metrics as it
			// did in 23.10 and 24.10.
			if (forcePlainTextMetrics)
				x.Request.Headers.Remove("Accept");
			return true;
		});

		// Internal dispatcher looks up the InternalContext to call the appropriate controller
		app.Use((ctx, next) => internalDispatcher.InvokeAsync(ctx, next));

		app.MapGrpcService<PersistentSubscriptions>();
		app.MapGrpcService<Users>();
		app.MapGrpcService<Streams<TStreamId>>();
		app.MapGrpcService<ClusterGossip>();
		app.MapGrpcService<Elections>();
		app.MapGrpcService<Operations>();
		app.MapGrpcService<ClientGossip>();
		app.MapGrpcService<Monitoring>();
		app.MapGrpcService<ServerFeatures>();
		// enable redaction service on unix sockets only
		app.MapGrpcService<Redaction>().AddEndpointFilter(async (c, next) => {
			if (!c.HttpContext.IsUnixSocketConnection())
				return Results.BadRequest("Redaction is only available via Unix Sockets");
			return await next(c).ConfigureAwait(false);
		});
	}

	public void ConfigureServices(IServiceCollection services) {
		// Web-related registrations
		services.AddRouting();
		var authBuilder = services.AddAuthentication(o => {
			o.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
			if (OAuthEnabled) {
				o.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
			}

			o.AddScheme<EventStoreAuthenticationHandler>("kurrent auth", displayName: null);
		});
		if (OAuthEnabled) {
			var oidcConfig = _configuration.GetSection("KurrentDB:OAuth");
			authBuilder
				.AddCookie()
				.AddOpenIdConnect(o => {
					var insecure = bool.TryParse(oidcConfig["Insecure"], out var insecureValue) && insecureValue;
					o.Authority = oidcConfig["Issuer"];
					o.ClientId = oidcConfig["ClientId"];
					o.ClientSecret = oidcConfig["ClientSecret"];
					o.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
					o.SignOutScheme = OpenIdConnectDefaults.AuthenticationScheme;
					o.ResponseType = OpenIdConnectResponseType.Code;
					if (insecure) {
						o.BackchannelHttpHandler = new HttpClientHandler {
							ServerCertificateCustomValidationCallback = (_, _, _, _) => true
						};
						o.RequireHttpsMetadata = false;
					}
					o.SaveTokens = true;
					o.GetClaimsFromUserInfoEndpoint = true;
					o.TokenValidationParameters.NameClaimType = JwtRegisteredClaimNames.Name;
					o.Events.OnUserInformationReceived = async ctx => {
						if (ctx.Principal != null) {
							await ctx.HttpContext.SignInAsync(ctx.Principal, ctx.Properties);
						}
					};
				});
		} else {
			authBuilder.AddCookie(x => x.LoginPath = "/ui/login");
		}

		services.AddAuthorization();
		services.AddAntiforgery(s => s.Cookie.Expiration = TimeSpan.Zero);
		services
			.AddSingleton<AuthenticationMiddleware>()
			.AddSingleton<AuthorizationMiddleware>()
			.AddSingleton(new KestrelToInternalBridgeMiddleware(_httpService.UriRouter, _httpService.LogHttpRequests, _httpService.AdvertiseAsHost, _httpService.AdvertiseAsPort))
			.AddSingleton(_authenticationProvider)
			.AddSingleton(_authorizationProvider);
		services.AddCors(o => o.AddPolicy(
			"default",
			b => b.AllowAnyOrigin().WithMethods(HttpMethod.Options, HttpMethod.Get).AllowAnyHeader())
		);

		// Other dependencies
		services
			.AddSingleton<ISubscriber>(_mainBus)
			.AddSingleton<IPublisher>(_mainQueue)
			.AddSingleton(new Streams<TStreamId>(_mainQueue,
				Ensure.Positive(_options.Application.MaxAppendSize),
				Ensure.Positive(_options.Application.MaxAppendEventSize),
				TimeSpan.FromMilliseconds(_options.Database.WriteTimeoutMs),
				_expiryStrategy, _trackers.GrpcTrackers, _authorizationProvider))
			.AddSingleton(new PersistentSubscriptions(_mainQueue, _authorizationProvider))
			.AddSingleton(new Users(_mainQueue, _authorizationProvider))
			.AddSingleton(new Operations(_mainQueue, _authorizationProvider))
			.AddSingleton(new ClusterGossip(_mainQueue, _authorizationProvider, _clusterDns,
				updateTracker: _trackers.GossipTrackers.ProcessingPushFromPeer,
				readTracker: _trackers.GossipTrackers.ProcessingRequestFromPeer))
			.AddSingleton(new Elections(_mainQueue, _authorizationProvider, _clusterDns))
			.AddSingleton(new ClientGossip(_mainQueue, _authorizationProvider, _trackers.GossipTrackers.ProcessingRequestFromGrpcClient))
			.AddSingleton(new Monitoring(_monitoringQueue))
			.AddSingleton(new Redaction(_mainQueue, _authorizationProvider))
			.AddSingleton<ServerFeatures>();

		// OpenTelemetry
		services.AddOpenTelemetry()
			.WithMetrics(meterOptions => meterOptions
				.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(_metricsConfiguration.ServiceName))
				.AddMeter(_metricsConfiguration.Meters)
				.AddView(ViewConfig)
				.AddInternalExporter()
				.AddPrometheusExporter(options => {
					if (_metricsConfiguration is { LegacyCoreNaming: true, LegacyProjectionsNaming: true }) {
						options.DisableTotalNameSuffixForCounters = true;
					} else if (_metricsConfiguration.LegacyCoreNaming || _metricsConfiguration.LegacyProjectionsNaming) {
						Log.Error("Inconsistent Meter names: {names}. Please use EventStore or KurrentDB for all.",
							string.Join(", ", _metricsConfiguration.Meters));
					}

					options.ScrapeResponseCacheDurationMilliseconds = 1000;
				}));

		// gRPC
		services
			.AddSingleton<RetryInterceptor>()
			.AddGrpc(options => options.Interceptors.Add<RetryInterceptor>())
			.AddServiceOptions<Streams<TStreamId>>(options => options.MaxReceiveMessageSize = TFConsts.EffectiveMaxLogRecordSize);

		// Ask the node itself to add DI registrations
		_configureNodeServices(services);

		// Let pluggable components to register their services
		foreach (var component in _plugableComponents)
			component.ConfigureServices(services, _configuration);
		return;

		MetricStreamConfiguration? ViewConfig(Instrument i) {
			if (i.Name == MetricsBootstrapper.LogicalChunkReadDistributionName(_metricsConfiguration.ServiceName))
				// 20 buckets, 0, 1, 2, 4, 8, ...
				return new ExplicitBucketHistogramConfiguration { Boundaries = [0, .. Enumerable.Range(0, count: 19).Select(x => 1 << x)] };
			if (i.Name.StartsWith(_metricsConfiguration.ServiceName + "-") && (i.Name.EndsWith("-latency-seconds") || i.Name.EndsWith("-latency") && i.Unit == "seconds"))
				return new ExplicitBucketHistogramConfiguration {
					Boundaries = [
						0.001, //    1 ms
						0.005, //    5 ms
						0.01, //   10 ms
						0.05, //   50 ms
						0.1, //  100 ms
						0.5, //  500 ms
						1, // 1000 ms
						5, // 5000 ms
					]
				};
			if (i.Name.StartsWith(_metricsConfiguration.ServiceName + "-") && (i.Name.EndsWith("-seconds") || i.Unit == "seconds"))
				return new ExplicitBucketHistogramConfiguration {
					Boundaries = [
						0.000_001, // 1 microsecond
						0.000_01, 0.000_1, 0.001, // 1 millisecond
						0.01, 0.1, 1, // 1 second
						10,
					]
				};
			return default;
		}
	}

	public void Handle(SystemMessage.SystemReady _) => _ready = true;

	public void Handle(SystemMessage.BecomeShuttingDown _) => _ready = false;

	private class StatusCheck {
		private readonly ClusterVNodeStartup<TStreamId> _startup;
		private readonly int _livecode = 204;

		public StatusCheck(ClusterVNodeStartup<TStreamId> startup) {
			_startup = startup ?? throw new ArgumentNullException(nameof(startup));
		}

		public void Configure(IApplicationBuilder builder) =>
			builder.Use(GetAndHeadOnly)
				.UseRouter(router => router
					.MapMiddlewareGet("live", inner => inner.Use(Live)));

		private MidFunc Live => (context, next) => {
			if (_startup._ready) {
				if (context.Request.Query.TryGetValue("liveCode", out var expected) &&
				    int.TryParse(expected, out var statusCode)) {
					context.Response.StatusCode = statusCode;
				} else {
					context.Response.StatusCode = _livecode;
				}
			} else {
				context.Response.StatusCode = 503;
			}

			return Task.CompletedTask;
		};

		private static MidFunc GetAndHeadOnly => (context, next) => {
			switch (context.Request.Method) {
				case "HEAD":
					context.Request.Method = "GET";
					return next();
				case "GET":
					return next();
				default:
					context.Response.StatusCode = 405;
					return Task.CompletedTask;
			}
		};
	}

	bool OAuthEnabled => _plugableComponents.Any(x => x is { Enabled: true, Name: "OAuthAuthentication" });
}
