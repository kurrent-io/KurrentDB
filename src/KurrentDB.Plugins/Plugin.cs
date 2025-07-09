// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics;
using EventStore.Plugins.Diagnostics;
using EventStore.Plugins.Licensing;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using static System.StringComparison;
using static EventStore.Plugins.Diagnostics.PluginDiagnosticsDataCollectionMode;

namespace EventStore.Plugins;

public record PluginOptions {
	public string? Name { get; init; }
	public string? Version { get; init; }
	public string? LicensePublicKey { get; init; }
	public string[]? RequiredEntitlements { get; init; }
	public string? DiagnosticsName { get; init; }
	public KeyValuePair<string, object?>[] DiagnosticsTags { get; init; } = [];
}

[PublicAPI]
public abstract class Plugin : IPlugableComponent, IDisposable {
	protected Plugin(
		string? name = null,
		string? version = null,
		string? licensePublicKey = null,
		string[]? requiredEntitlements = null,
		string? diagnosticsName = null,
		params KeyValuePair<string, object?>[] diagnosticsTags) {

		var pluginType = GetType();

		Name = name ?? pluginType.Name
			.Replace("Plugin", "", OrdinalIgnoreCase)
			.Replace("Provider", "", OrdinalIgnoreCase)
			.Replace("Component", "", OrdinalIgnoreCase)
			.Replace("Subsystems", "", OrdinalIgnoreCase)
			.Replace("Subsystem", "", OrdinalIgnoreCase);

		Version = GetPluginVersion(version, pluginType);

		LicensePublicKey = licensePublicKey ?? LicenseConstants.LicensePublicKey;
		RequiredEntitlements = requiredEntitlements;

		DiagnosticsName = diagnosticsName ?? Name;
		DiagnosticsTags = diagnosticsTags;

		DiagnosticListener = new(DiagnosticsName);

		IsEnabledResult = (true, "");
		Configuration = null!;

		return;

		static string GetPluginVersion(string? pluginVersion, Type pluginType) {
			const string emptyVersion = "0.0.0.0";
			const string defaultVersion = "1.0.0.0-preview";

			var version = pluginVersion
			              ?? pluginType.Assembly.GetName().Version?.ToString()
			              ?? emptyVersion;

			return version != emptyVersion ? version : defaultVersion;
		}
	}

	protected Plugin(PluginOptions options) : this(
		options.Name,
		options.Version,
		options.LicensePublicKey,
		options.RequiredEntitlements,
		options.DiagnosticsName,
		options.DiagnosticsTags) { }

	public string LicensePublicKey { get; }

	public string[]? RequiredEntitlements { get; }

	DiagnosticListener DiagnosticListener { get; }

	(bool Enabled, string EnableInstructions) IsEnabledResult { get; set; }

	IConfiguration Configuration { get; set; }

	/// <inheritdoc />
	public string Name { get; }

	/// <inheritdoc />
	public string Version { get; }

	/// <inheritdoc />
	public string DiagnosticsName { get; }

	/// <inheritdoc />
	public KeyValuePair<string, object?>[] DiagnosticsTags { get; }

	/// <inheritdoc />
	public bool Enabled => IsEnabledResult.Enabled;

	public virtual void ConfigureServices(IServiceCollection services, IConfiguration configuration) { }

	public virtual void ConfigureApplication(IApplicationBuilder app, IConfiguration configuration) { }

	protected virtual void OnLicenseException(Exception ex, Action<Exception> shutdown) {
		shutdown(ex);
	}

	/// <summary>
	///		This check will happen before the plugin is configured and returns true by default.<br/>
	///		Nonetheless the plugin can still be disabled by calling <see cref="Disable"/> on ConfigureServices and ConfigureApplication.
	/// </summary>
	/// <param name="configuration">The configuration of the application.<br/></param>
	public virtual (bool Enabled, string EnableInstructions) IsEnabled(IConfiguration configuration) => IsEnabledResult;

	public void Disable(string reason) => IsEnabledResult = (false, reason);

	void IPlugableComponent.ConfigureServices(IServiceCollection services, IConfiguration configuration) {
		Configuration = configuration;
		IsEnabledResult = IsEnabled(configuration);

		if (Enabled)
			ConfigureServices(services, configuration);

		PublishDiagnosticsData(new() { ["enabled"] = Enabled }, Partial);
	}

	void IPlugableComponent.ConfigureApplication(IApplicationBuilder app, IConfiguration configuration) {
		var logger = app.ApplicationServices.GetRequiredService<ILoggerFactory>().CreateLogger(GetType());

		// if the plugin is disabled, just get out
		if (!Enabled) {
			logger.LogInformation(
				"{PluginName} {Version} plugin disabled. {EnableInstructions}",
				Name, Version, IsEnabledResult.EnableInstructions
			);

			return;
		}

		if (Enabled && RequiredEntitlements is not null) {
			// the plugin is enabled and requires a license
			var licenseService = app.ApplicationServices.GetRequiredService<ILicenseService>();

			_ = LicenseMonitor.MonitorAsync(
				Name,
				RequiredEntitlements,
				licenseService,
				ex => OnLicenseException(ex, licenseService.RejectLicense),
				logger,
				LicensePublicKey);
		}

		// there is still a chance to disable the plugin when configuring the application
		// this is useful when the plugin is enabled, but some conditions that can only be checked here are not met
		ConfigureApplication(app, Configuration);

		// at this point we know if the plugin is enabled and configured or not
		if (Enabled)
			logger.LogInformation("{PluginName} {Version} plugin enabled.", Name, Version);
		else {
			logger.LogInformation(
				"{PluginName} {Version} plugin disabled. {EnableInstructions}",
				Name, Version, IsEnabledResult.EnableInstructions
			);
		}

		// finally publish diagnostics data
		PublishDiagnosticsData(new() { ["enabled"] = Enabled }, Partial);
	}

	/// <summary>
	///   Publishes diagnostics data as a snapshot.<br/>
	///   Uses the <see cref="PluginDiagnosticsData"/> container.<br/>
	///   Multiple calls to this method will overwrite the previous snapshot.<br/>
	///   Used for ESDB telemetry.
	/// </summary>
	/// <param name="eventData">The data to publish.</param>
	/// <param name="mode">The mode of data collection for a plugin event.</param>
	protected internal void PublishDiagnosticsData(Dictionary<string, object?> eventData, PluginDiagnosticsDataCollectionMode mode = Partial) {
		var value = new PluginDiagnosticsData {
			Source = DiagnosticsName,
			Data = eventData,
			CollectionMode = mode
		};

		DiagnosticListener.Write(nameof(PluginDiagnosticsData), value);
	}

	/// <summary>
	///   Publishes diagnostics data. <br/>
	///   Uses the same <see cref="PluginDiagnosticsData"/> container as snapshot diagnostics. <br/>
	///   The event 'PluginDiagnosticsData' is reserved for the default snapshot diagnostics data.
	/// </summary>
	/// <param name="eventName">The name of the event to publish.</param>
	/// <param name="eventData">The data to publish.</param>
	/// <param name="mode">The mode of data collection for a plugin event.</param>
	protected internal void PublishDiagnosticsData(string eventName, Dictionary<string, object?> eventData, PluginDiagnosticsDataCollectionMode mode = Event) {
		if (eventName == nameof(PluginDiagnosticsData))
		    throw new ArgumentException("Event name cannot be PluginDiagnosticsData", nameof(eventName));

		DiagnosticListener.Write(
			eventName,
			new PluginDiagnosticsData{
				Source = DiagnosticsName,
				EventName = eventName,
				Data = eventData,
				CollectionMode = mode
			}
		);
	}

	/// <summary>
	///		Publishes diagnostics events. <br/>
	/// </summary>
	/// <param name="pluginEvent"></param>
	/// <typeparam name="T"></typeparam>
	protected internal void PublishDiagnosticsEvent<T>(T pluginEvent) =>
		DiagnosticListener.Write(typeof(T).Name, pluginEvent);

	/// <inheritdoc />
	public void Dispose() => DiagnosticListener.Dispose();
}
