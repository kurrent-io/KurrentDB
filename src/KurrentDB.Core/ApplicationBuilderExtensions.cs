// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable

using System.Linq;
using DotNext.Collections.Generic;
using KurrentDB.Core.Services.Transport.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace KurrentDB.Core;

public static class ApplicationBuilderExtensions {
	// Maps all the legacy pipeline routes to the legacy pipeline
	// If we don't register them as routes MapStaticAssets intercepts some that it shouldn't
	public static IEndpointRouteBuilder MapLegacyPipeline(
		this IEndpointRouteBuilder app,
		IHttpService httpService,
		InternalDispatcherEndpoint internalDispatcher,
		bool forcePlainTextMetrics) {

		// map routes so that MapAssets doesn't intercept.
		var legacyPipeline = ((IApplicationBuilder)app)
			.New()
			.UseLegacyPipeline(internalDispatcher, forcePlainTextMetrics)
			.Build();

		httpService
			.Actions
			.Select(action => ConvertToRoute(action.UriTemplate))
			.Distinct()
			.ForEach(pattern => {
				app.Map(pattern, legacyPipeline);
			});

		return app;
	}

	static string ConvertToRoute(string uriTemplate) {
		var route = uriTemplate.Split('?').First();
		return System.Net.WebUtility.UrlDecode(route.EndsWith('/') ? route[..^1] : route);
	}

	static IApplicationBuilder UseLegacyPipeline(
		this IApplicationBuilder app,
		InternalDispatcherEndpoint internalDispatcher,
		bool forcePlainTextMetrics) {

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

		return app;
	}
}
