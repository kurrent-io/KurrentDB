// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable AccessToDisposedClosure

using System.Reactive.Subjects;
using System.Security.Cryptography;
using EventStore.Plugins.Diagnostics;
using EventStore.Plugins.Licensing;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using static System.Convert;
using License = EventStore.Plugins.Licensing.License;

namespace EventStore.Plugins.Tests;

public class PluginBaseTests {
	[Fact]
	public void plugin_base_sets_defaults_automatically() {
		var expectedOptions = new PluginOptions {
			Name = "NightCity",
			Version = "1.0.0.0",
			DiagnosticsName = "NightCity",
		};

		using var plugin = new NightCityPlugin();

		plugin.Options.Should().BeEquivalentTo(expectedOptions);
	}

	[Fact]
	public void subsystems_plugin_base_sets_defaults_automatically() {
		var expectedOptions = new SubsystemsPluginOptions {
			Name = "PhantomLiberty",
			Version = "1.0.0.0",
			DiagnosticsName = "PhantomLiberty",
			CommandLineName = "phantom-liberty"
		};

		using var plugin = new PhantomLibertySubsystemsPlugin();

		plugin.Options.Should().BeEquivalentTo(expectedOptions);
	}

	[Fact]
	public void plugin_diagnostics_snapshot_is_not_overriden_internally() {
		// Arrange
		var userDiagnosticsData = new Dictionary<string, object?> {
			["first_value"]  = 1,
			["second_value"] = 2
		};

		IPlugableComponent plugin = new NightCityPlugin(new(){ Name = Guid.NewGuid().ToString() }) {
			OnConfigureServices = x => x.PublishDiagnosticsData(userDiagnosticsData),
			OnConfigureApplication = x => x.Disable("Disabled on ConfigureApplication because I can")
		};

		using var collector = PluginDiagnosticsDataCollector.Start(plugin.DiagnosticsName);

		var builder = WebApplication.CreateBuilder();

		plugin.ConfigureServices(builder.Services, builder.Configuration);

		using var app = builder.Build();

		// Act & Assert
		plugin.ConfigureApplication(app, app.Configuration);

		var expectedDiagnosticsData = new Dictionary<string, object?>(userDiagnosticsData) {
			["enabled"] = false
		};

		collector.CollectedEvents(plugin.DiagnosticsName).Should().ContainSingle()
			.Which.Data.Should().BeEquivalentTo(expectedDiagnosticsData);
	}

	[Fact]
	public void commercial_plugin_is_disabled_when_licence_is_missing() {
		// Arrange
		var licenseService = new FakeLicenseService(createLicense: false);

		IPlugableComponent plugin = new NightCityPlugin(new() {
			RequiredEntitlements = ["starlight"],
		});

		var builder = WebApplication.CreateBuilder();

		builder.Services.AddSingleton<ILicenseService>(licenseService);

		plugin.ConfigureServices(builder.Services, builder.Configuration);

		using var app = builder.Build();

		// Act
		plugin.ConfigureApplication(app, app.Configuration);

		// Assert
		licenseService.RejectionException.Should().BeOfType<LicenseException>().Which
			.FeatureName.Should().Be(plugin.Name);
	}

	[Fact]
	public void commercial_plugin_is_disabled_when_licence_is_missing_entitlement() {
		// Arrange
		var licenseService = new FakeLicenseService(createLicense: true);

		IPlugableComponent plugin = new NightCityPlugin(new() {
			RequiredEntitlements = ["starlight"],
		});

		var builder = WebApplication.CreateBuilder();

		builder.Services.AddSingleton<ILicenseService>(licenseService);

		plugin.ConfigureServices(builder.Services, builder.Configuration);

		using var app = builder.Build();

		// Act
		plugin.ConfigureApplication(app, app.Configuration);

		// Assert
		licenseService.RejectionException.Should().BeOfType<LicenseEntitlementException>().Which
			.FeatureName.Should().Be(plugin.Name);
	}

	[Fact]
	public void plugin_can_implement_its_own_license_error_handling() {
		// Arrange
		var licenseService = new FakeLicenseService(createLicense: true);

		Exception? licenseException = null;
		IPlugableComponent plugin = new CustomCityPlugin(ex => licenseException = ex);

		var builder = WebApplication.CreateBuilder();

		builder.Services.AddSingleton<ILicenseService>(licenseService);

		plugin.ConfigureServices(builder.Services, builder.Configuration);

		using var app = builder.Build();

		// Act
		plugin.ConfigureApplication(app, app.Configuration);

		// Assert
		licenseService.RejectionException.Should().BeNull();
		licenseException.Should().BeOfType<LicenseEntitlementException>().Which
			.FeatureName.Should().Be(plugin.Name);
	}

	[Fact]
	public void commercial_plugin_is_enabled_when_licence_is_present() {
		// Arrange
		var licenseService = new FakeLicenseService(createLicense: true, "starlight");

		IPlugableComponent plugin = new NightCityPlugin(new() {
			RequiredEntitlements = ["starlight"],
		});

		var builder = WebApplication.CreateBuilder();

		builder.Services.AddSingleton<ILicenseService>(licenseService);

		plugin.ConfigureServices(builder.Services, builder.Configuration);

		using var app = builder.Build();

		var configure = () => plugin.ConfigureApplication(app, builder.Configuration);

		// Act & Assert
		configure.Should().NotThrow<Exception>();
	}

	[Fact]
	public void plugin_can_be_disabled_on_ConfigureServices() {
		// Arrange
		IPlugableComponent plugin = new NightCityPlugin(new(){ Name = Guid.NewGuid().ToString() }) {
			OnConfigureServices = x => x.Disable("Disabled on ConfigureServices because I can")
		};

		using var collector = PluginDiagnosticsDataCollector.Start(plugin.DiagnosticsName);

		var builder = WebApplication.CreateBuilder();

		// Act & Assert
		plugin.Enabled.Should().BeTrue();

		plugin.ConfigureServices(builder.Services, builder.Configuration);

		plugin.Enabled.Should().BeFalse();

		collector.CollectedEvents(plugin.DiagnosticsName).Should().ContainSingle()
			.Which.Data.Should().ContainKey("enabled")
			.WhoseValue.Should().BeEquivalentTo(false);
	}

	[Fact]
	public void plugin_can_be_disabled_on_ConfigureApplication() {
		// Arrange
		IPlugableComponent plugin = new NightCityPlugin(new(){ Name = Guid.NewGuid().ToString() }) {
			OnConfigureApplication = x => x.Disable("Disabled on ConfigureApplication because I can")
		};

		using var collector = PluginDiagnosticsDataCollector.Start(plugin.DiagnosticsName);

		var builder = WebApplication.CreateBuilder();

		plugin.ConfigureServices(builder.Services, builder.Configuration);

		using var app = builder.Build();

		// Act & Assert
		plugin.Enabled.Should().BeTrue();

		plugin.ConfigureApplication(app, app.Configuration);

		plugin.Enabled.Should().BeFalse();

		collector.CollectedEvents(plugin.DiagnosticsName).Should().ContainSingle()
			.Which.Data.Should().ContainKey("enabled")
			.WhoseValue.Should().BeEquivalentTo(false);
	}

	public class FakeLicenseService : ILicenseService {
		public FakeLicenseService(
			bool createLicense,
			params string[] entitlements) {

			SelfLicense = License.Create([]);

			if (createLicense) {
				CurrentLicense = License.Create(entitlements.ToDictionary(
					x => x,
					x => (object)"true"));
				Licenses = new BehaviorSubject<License>(CurrentLicense);
			} else {
				CurrentLicense = null;
				var licenses = new Subject<License>();
				licenses.OnError(new Exception("license expired, say"));
				Licenses = licenses;
			}
		}

		public License SelfLicense { get; }

		public License? CurrentLicense { get; }

		public IObservable<License> Licenses { get; }

		public void RejectLicense(Exception ex) {
			RejectionException = ex;
		}

		public Exception? RejectionException { get; private set; }
	}

	class NightCityPlugin : Plugin {
		public NightCityPlugin(PluginOptions options) : base(options) {
			Options = options with {
				Name = Name,
				Version = Version,
				RequiredEntitlements = RequiredEntitlements,
				DiagnosticsName = DiagnosticsName,
			};
		}

		public NightCityPlugin() : this(new()) { }

		public PluginOptions Options { get; }

		public Action<Plugin>? OnConfigureServices    { get; set; }
		public Action<Plugin>? OnConfigureApplication { get; set; }

		public override void ConfigureServices(IServiceCollection services, IConfiguration configuration) =>
			OnConfigureServices?.Invoke(this);

		public override void ConfigureApplication(IApplicationBuilder app, IConfiguration configuration) =>
			OnConfigureApplication?.Invoke(this);
	}

	class CustomCityPlugin : NightCityPlugin {
		private readonly Action<Exception> _onLicenseException;

		public CustomCityPlugin(Action<Exception> onLicenseException) : base(new() {
			RequiredEntitlements = ["starlight"]
		}) {
			_onLicenseException = onLicenseException;
		}
		protected override void OnLicenseException(Exception ex, Action<Exception> shutdown) {
			_onLicenseException(ex);
		}
	}

	class PhantomLibertySubsystemsPlugin : SubsystemsPlugin {
		public PhantomLibertySubsystemsPlugin(SubsystemsPluginOptions options) : base(options) {
			Options = options with {
				Name = Name,
				Version = Version,
				DiagnosticsName = DiagnosticsName,
				CommandLineName = CommandLineName
			};
		}

		public PhantomLibertySubsystemsPlugin() : this(new()) { }

		public SubsystemsPluginOptions Options { get; }
	}
}
