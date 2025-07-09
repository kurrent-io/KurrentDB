// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Plugins.Subsystems;
using static System.StringComparison;

namespace EventStore.Plugins;

public record SubsystemsPluginOptions : PluginOptions {
	public string? CommandLineName { get; init; }
}

[PublicAPI]
public abstract class SubsystemsPlugin : Plugin, ISubsystem, ISubsystemsPlugin {
	protected SubsystemsPlugin(SubsystemsPluginOptions options) : base(options) {
		CommandLineName = options.CommandLineName ?? options.Name ?? GetType().Name
			.Replace("Plugin", "", OrdinalIgnoreCase)
			.Replace("Component", "", OrdinalIgnoreCase)
			.Replace("Subsystems", "", OrdinalIgnoreCase)
			.Replace("Subsystem", "", OrdinalIgnoreCase)
			.Kebaberize();
	}

	protected SubsystemsPlugin(
		string? name = null, string? version = null,
		string? licensePublicKey = null,
		string[]? requiredEntitlements = null,
		string? commandLineName = null,
		string? diagnosticsName = null,
		params KeyValuePair<string, object?>[] diagnosticsTags
	) : this(new() {
		Name = name,
		Version = version,
		LicensePublicKey = licensePublicKey,
		RequiredEntitlements = requiredEntitlements,
		DiagnosticsName = diagnosticsName,
		DiagnosticsTags = diagnosticsTags,
		CommandLineName = commandLineName
	}) { }

	public string CommandLineName { get; }

	public virtual Task Start() => Task.CompletedTask;

	public virtual Task Stop() => Task.CompletedTask;

	public virtual IReadOnlyList<ISubsystem> GetSubsystems() => [this];
}
