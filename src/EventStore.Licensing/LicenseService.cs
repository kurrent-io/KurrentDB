// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Plugins.Licensing;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace EventStore.Licensing;

public class LicenseService : ILicenseService {
	private static readonly ILogger Log = Serilog.Log.ForContext<LicenseService>();
	private readonly IHostApplicationLifetime _lifetime;
	private readonly Action<Exception> _requestShutdown;

	public LicenseService(
		IHostApplicationLifetime lifetime,
		Action<Exception> requestShutdown,
		ILicenseProvider licenseProvider) {

		_lifetime = lifetime;
		_requestShutdown = requestShutdown;
		SelfLicense = License.Create([]);
		Licenses = licenseProvider.Licenses;
		Licenses.Subscribe(
			license => {
				CurrentLicense = license;
			},
			ex => {
				CurrentLicense = null;
			});
	}

	public License SelfLicense { get; private set; }

	public License? CurrentLicense { get; private set; }

	public IObservable<License> Licenses { get; private set; }

	public void RejectLicense(Exception ex) {
		// we wait for the application to start before stopping it so that
		// 1. we can log the error message after stopping, to be really clear what the cause was
		// 2. stopping the application during startup results in several other exceptions being thrown which are just noise
		_lifetime.ApplicationStarted.Register(() => {
			_requestShutdown(ex);
		});
		_lifetime.ApplicationStopped.Register(() => Log.Fatal(ex.Message));

		// belt and braces (in case the above fails due to application somehow never fully starting)
		Task.Run(async () => {
			await Task.Delay(TimeSpan.FromMinutes(1));
			Log.Fatal(ex.Message);
			_lifetime.StopApplication();
		});
	}
}
