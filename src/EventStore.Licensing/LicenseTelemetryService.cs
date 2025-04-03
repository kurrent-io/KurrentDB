// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Plugins.Licensing;
using Microsoft.Extensions.Hosting;

namespace EventStore.Licensing;

public class LicenseTelemetryService : IHostedService {
	private readonly ILicenseService _licenseProvider;
	private readonly Action<Dictionary<string, object?>> _publish;

	public LicenseTelemetryService(
		ILicenseService licenseProvider,
		Action<Dictionary<string, object?>> publish) {

		_licenseProvider = licenseProvider;
		_publish = publish;
	}

	public Task StartAsync(CancellationToken cancellationToken) {
		License? current = null;
		_licenseProvider.Licenses.Subscribe(
			onNext: license => {
				current = license;
				var telemetry = LicenseSummary.SelectForTelemetry(license);
				_publish(telemetry);
			},
			onError: ex => {
				current = null;
				_publish([]);
			});

		_ = TelemetryLoop();

		return Task.CompletedTask;

		async Task TelemetryLoop() {
			while (true) {
				await Task.Delay(TimeSpan.FromHours(1), cancellationToken);
				if (current is { } currentLicense) {
					var telemetry = LicenseSummary.SelectForTelemetry(currentLicense);
					_publish(telemetry);
				}
			}
		}
	}

	public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
