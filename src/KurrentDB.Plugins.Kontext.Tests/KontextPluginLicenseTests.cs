// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Reactive.Subjects;
using EventStore.Plugins;
using EventStore.Plugins.Licensing;
using KurrentDB.Common.Configuration;
using KurrentDB.Core.Configuration.Sources;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KurrentDB.Plugins.Kontext.Tests;

public class KontextPluginLicenseTests {
	[Test]
	public async Task Plugin_Name_Matches_PluginNames_Kontext() {
		var plugin = new KontextPlugin();
		await Assert.That(plugin.Name).IsEqualTo(PluginNames.Kontext);
	}

	[Test]
	public async Task Plugin_Requires_KONTEXT_Entitlement() {
		var plugin = new KontextPlugin();
		await Assert.That(plugin.RequiredEntitlements).IsNotNull();
		await Assert.That(plugin.RequiredEntitlements!).Contains("KONTEXT");
	}

	[Test]
	public async Task Plugin_Rejects_License_When_KONTEXT_Entitlement_Missing() {
		var licenseService = new FakeLicenseService(createLicense: true, "OTHER_FEATURE");
		await RunPluginLifecycle(licenseService);

		await Assert.That(licenseService.RejectionException).IsNotNull();
	}

	[Test]
	public async Task Plugin_Accepts_License_With_KONTEXT_Entitlement() {
		var licenseService = new FakeLicenseService(createLicense: true, "KONTEXT");
		await RunPluginLifecycle(licenseService);

		await Assert.That(licenseService.RejectionException).IsNull();
	}

	[Test]
	public async Task Plugin_Skipped_When_Not_Enabled_In_Config() {
		// No license should be required if the plugin is not enabled.
		var licenseService = new FakeLicenseService(createLicense: false);
		await RunPluginLifecycle(licenseService, enabled: false);

		await Assert.That(licenseService.RejectionException).IsNull();
	}

	static async Task RunPluginLifecycle(ILicenseService licenseService, bool enabled = true) {
		IPlugableComponent plugin = new KontextPlugin();

		var builder = WebApplication.CreateBuilder();
		builder.Services.AddSingleton(licenseService);
		builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> {
			[$"{KurrentConfigurationKeys.Prefix}:Kontext:Enabled"] = enabled ? "true" : "false",
			// Path override bypasses ResolveKontextDirectory's ClusterVNodeOptions dependency.
			[$"{KurrentConfigurationKeys.Prefix}:Kontext:Path"] =
				Path.Combine(Path.GetTempPath(), $"kontext-license-{Guid.NewGuid():N}"),
		});

		plugin.ConfigureServices(builder.Services, builder.Configuration);

		await using var app = builder.Build();
		app.UseRouting();
		plugin.ConfigureApplication(app, app.Configuration);

		// Give the LicenseMonitor a tick to evaluate.
		await Task.Delay(50);
	}

	sealed class FakeLicenseService : ILicenseService {
		public FakeLicenseService(bool createLicense, params string[] entitlements) {
			SelfLicense = License.Create([]);
			if (createLicense) {
				CurrentLicense = License.Create(
					entitlements.ToDictionary(e => e, e => (object)"true"));
				Licenses = new BehaviorSubject<License>(CurrentLicense);
			} else {
				CurrentLicense = null;
				var licenses = new Subject<License>();
				licenses.OnError(new Exception("license expired"));
				Licenses = licenses;
			}
		}

		public License SelfLicense { get; }
		public License? CurrentLicense { get; }
		public IObservable<License> Licenses { get; }
		public Exception? RejectionException { get; private set; }
		public void RejectLicense(Exception ex) => RejectionException = ex;
	}
}
