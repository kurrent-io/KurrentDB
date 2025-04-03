// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Reactive.Subjects;
using EventStore.Licensing.Keygen;
using EventStore.Plugins;
using EventStore.Plugins.Licensing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RestSharp;
using RestSharp.Authenticators.OAuth2;
using Serilog;

namespace EventStore.Licensing;

// circumventing the license mechanism is against the ESLv2 license.
public class LicensingPlugin : Plugin {
	private static readonly ILogger Log = Serilog.Log.ForContext<LicensingPlugin>();

	private readonly ILicenseProvider? _licenseProvider;
	private readonly Action<Exception> _requestShutdown;

	public LicensingPlugin(Action<Exception> requestShutdown) : this(requestShutdown, null) {
	}

	public LicensingPlugin(Action<Exception> requestShutdown, ILicenseProvider? licenseProvider) : base() {
		_licenseProvider = licenseProvider;
		_requestShutdown = requestShutdown;
	}


	public override void ConfigureApplication(IApplicationBuilder builder, IConfiguration configuration) {
		var licenseService = builder.ApplicationServices.GetRequiredService<ILicenseService>();

		License? currentLicense = null;
		Exception? licenseError = null;
		licenseService.Licenses.Subscribe(
			license => {
				currentLicense = license;
				licenseError = null;
			},
			ex => {
				currentLicense = null;
				licenseError = ex;
			});

		builder.UseEndpoints(endpoints => endpoints
			.MapGet("/license", (HttpContext context) => {
				if (currentLicense is { } license )
					return Results.Json(LicenseSummary.SelectForEndpoint(license));

				if (licenseError is NoLicenseKeyException)
					return Results.NotFound();

				return Results.NotFound(licenseError?.Message ?? "Unknown eror");
			})
			.RequireAuthorization());
	}

	public override void ConfigureServices(IServiceCollection services, IConfiguration configuration) {
		var baseUrl = $"https://licensing.kurrent.io/v1/";
		var clientOptions = configuration.GetSection("KurrentDB").Get<KeygenClientOptions>() ?? new();
		if (clientOptions.Licensing.BaseUrl is not null) {
			baseUrl = clientOptions.Licensing.BaseUrl;
			Log.Information("Using custom licensing URL: {Url}. This requires permission from Kurrent, Inc.", baseUrl);
		}

		IObservable<LicenseInfo> licenses;

		if (string.IsNullOrWhiteSpace(clientOptions.Licensing.LicenseKey)) {
			var sub = new Subject<LicenseInfo>();
			sub.OnError(new NoLicenseKeyException());
			licenses = sub;
		} else {
			var lifecycleService = new KeygenLifecycleService(
				new KeygenClient(
					clientOptions,
					restClient: new RestClient(
						new RestClientOptions(baseUrl) {
							Authenticator = new OAuth2AuthorizationRequestHeaderAuthenticator(clientOptions.Licensing.LicenseKey, "License"),
							// todo: Interceptors = [new KeygenSignatureInterceptor()],
						})),
				new Fingerprint(clientOptions.Licensing.IncludePortInFingerprint ? clientOptions.NodePort : null),
				revalidationDelay: TimeSpan.FromSeconds(10));

			licenses = lifecycleService.Licenses;
			services.AddHostedService(sp => lifecycleService);
		}

		services
			// other components such as plugins can use this to subscribe to the licenses, inspect them, and reject them
			.AddSingleton<ILicenseService>(sp => {
				var licenseProvider =
					_licenseProvider ??
					new KeygenLicenseProvider(licenses);

				return new LicenseService(
					sp.GetRequiredService<IHostApplicationLifetime>(),
					_requestShutdown,
					licenseProvider);
			})
			.AddHostedService(sp => new LicenseTelemetryService(
				sp.GetRequiredService<ILicenseService>(),
				telemetry => PublishDiagnosticsData(telemetry, Plugins.Diagnostics.PluginDiagnosticsDataCollectionMode.Snapshot)));
	}

	class NoLicenseKeyException : Exception {
	}
}
