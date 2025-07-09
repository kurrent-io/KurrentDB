// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Plugins.Licensing;
using Microsoft.Extensions.Logging.Testing;

namespace EventStore.Plugins.Tests.Licensing;

public class LicenseMonitorTests {
	[Fact]
	public async Task valid_license_with_correct_entitlements() {
		var licenseService = new PluginBaseTests.FakeLicenseService(
			createLicense: true,
			entitlements: ["MY_ENTITLEMENT"]);

		Exception? licenseException = null;
		var criticalError = false;

		using var subscription = await LicenseMonitor.MonitorAsync(
			featureName: "TestFeature",
			requiredEntitlements: ["MY_ENTITLEMENT"],
			licenseService: licenseService,
			onLicenseException: ex => licenseException = ex,
			logger: new FakeLogger(),
			onCriticalError: _ => criticalError = true);

		licenseException.Should().BeNull();
		criticalError.Should().BeFalse();
	}

	[Fact]
	public async Task valid_license_with_all_entitlement() {
		var licenseService = new PluginBaseTests.FakeLicenseService(
			createLicense: true,
			entitlements: ["ALL"]);

		Exception? licenseException = null;
		var criticalError = false;

		using var subscription = await LicenseMonitor.MonitorAsync(
			featureName: "TestFeature",
			requiredEntitlements: ["MY_ENTITLEMENT"],
			licenseService: licenseService,
			onLicenseException: ex => licenseException = ex,
			logger: new FakeLogger(),
			onCriticalError: _ => criticalError = true);

		licenseException.Should().BeNull();
		criticalError.Should().BeFalse();
	}

	[Fact]
	public async Task valid_license_with_missing_entitlement() {
		var licenseService = new PluginBaseTests.FakeLicenseService(
			createLicense: true,
			entitlements: []);

		Exception? licenseException = null;
		var criticalError = false;

		using var subscription = await LicenseMonitor.MonitorAsync(
			featureName: "TestFeature",
			requiredEntitlements: ["MY_ENTITLEMENT"],
			licenseService: licenseService,
			onLicenseException: ex => licenseException = ex,
			logger: new FakeLogger(),
			onCriticalError: _ => criticalError = true);

		licenseException.Should().BeOfType<LicenseEntitlementException>()
			.Which.MissingEntitlement.Should().Be("MY_ENTITLEMENT");
		criticalError.Should().BeFalse();
	}

	[Fact]
	public async Task no_license() {
		var licenseService = new PluginBaseTests.FakeLicenseService(
			createLicense: false);

		Exception? licenseException = null;
		var criticalError = false;

		using var subscription = await LicenseMonitor.MonitorAsync(
			featureName: "TestFeature",
			requiredEntitlements: [],
			licenseService: licenseService,
			onLicenseException: ex => licenseException = ex,
			logger: new FakeLogger(),
			onCriticalError: _ => criticalError = true);

		licenseException.Should().BeOfType<LicenseException>();
		criticalError.Should().BeFalse();
	}

	[Fact]
	public async Task license_is_not_valid() {
		var licenseService = new PluginBaseTests.FakeLicenseService(
			createLicense: true,
			entitlements: []);

		Exception? licenseException = null;
		var criticalError = false;

		using var subscription = await LicenseMonitor.MonitorAsync(
			featureName: "TestFeature",
			requiredEntitlements: [],
			licenseService: licenseService,
			onLicenseException: ex => licenseException = ex,
			logger: new FakeLogger(),
			licensePublicKey: "a_different_public_key",
			onCriticalError: _ => criticalError = true);

		licenseException.Should().BeOfType<LicenseException>();
		criticalError.Should().BeTrue();
	}
}
