// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable

using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime;
using KurrentDB.Common.Utils;
using KurrentDB.Core;
using KurrentDB.Core.Configuration.Sources;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using Serilog.Events;
using ILogger = Serilog.ILogger;

namespace KurrentDB;

/// <summary>
/// What every host does before it builds a node: say what the node is, and decide whether it can run.
/// </summary>
public static class NodePreflight {
	// resolved per call rather than captured, because a host may configure Serilog after this type is
	// first touched
	static ILogger Log => Serilog.Log.ForContext(typeof(NodePreflight));

	/// <returns>
	/// True if a node can start. Otherwise false, with <paramref name="error"/> saying why, for the caller
	/// to report: the server executable logs it and exits, an embedded database throws it.
	/// </returns>
	public static bool TryPrepare(ClusterVNodeOptions options, [NotNullWhen(false)] out string? error) {
		var log = Log;

		if (!Environment.Is64BitProcess) {
			error = "KurrentDB requires a 64-bit process to run.";
			return false;
		}

		log.Information(
			"{description,-25} {version} {edition} ({buildId}/{commitSha}, {timestamp})", "DB VERSION:",
			VersionInfo.Version, VersionInfo.Edition, VersionInfo.BuildId, VersionInfo.CommitSha, VersionInfo.Timestamp
		);

		log.Information("{description,-25} {osArchitecture} ", "OS ARCHITECTURE:", System.Runtime.InteropServices.RuntimeInformation.OSArchitecture);
		log.Information("{description,-25} {osFlavor} ({osVersion})", "OS:", RuntimeInformation.OsPlatform, Environment.OSVersion);
		log.Information("{description,-25} {osRuntimeVersion} ({architecture}-bit)", "RUNTIME:", RuntimeInformation.RuntimeVersion, RuntimeInformation.RuntimeMode);
		log.Information("{description,-25} {maxGeneration} IsServerGC: {isServerGC} Latency Mode: {latencyMode}", "GC:",
			GC.MaxGeneration == 0 ? "NON-GENERATION (PROBABLY BOEHM)" : $"{GC.MaxGeneration + 1} GENERATIONS",
			GCSettings.IsServerGC,
			GCSettings.LatencyMode);
		log.Information("{description,-25} {logsDirectory}", "LOGS:", options.Logging.Log);
		log.Information("{description,-25} {isWindowsService}", "IsWindowsService:", WindowsServiceHelpers.IsWindowsService());

		var gcSettings = string.Join($"{Environment.NewLine}    ", GC.GetConfigurationVariables().Select(kvp => $"{kvp.Key}: {kvp.Value}"));
		log.Information($"GC Configuration settings:{Environment.NewLine}    {{settings}}", gcSettings);

		log.Information(options.DumpOptions()!);

		var level = options.Application.AllowUnknownOptions
			? LogEventLevel.Warning
			: LogEventLevel.Fatal;

		foreach (var (option, suggestion) in options.Unknown.Options) {
			if (string.IsNullOrEmpty(suggestion)) {
				log.Write(level, "The option {option} is not a known option.", option);
			} else {
				log.Write(level, "The option {option} is not a known option. Did you mean {suggestion}?", option, suggestion);
			}
		}

		if (options.UnknownOptionsDetected && !options.Application.AllowUnknownOptions) {
			error = "Found unknown options. To continue anyway, set " +
				$"{nameof(ClusterVNodeOptions.ApplicationOptions.AllowUnknownOptions)} to true.";
			return false;
		}

		WriteConfigurationWarnings(log, options);

		error = null;
		return true;
	}

	static void WriteConfigurationWarnings(ILogger log, ClusterVNodeOptions options) {
		var defaultLocationWarnings = options.CheckForLegacyDefaultLocations(LocationOptionWithLegacyDefault.SupportedLegacyLocations);
		foreach (var locationWarning in defaultLocationWarnings) {
			log.Warning(locationWarning);
		}

		var eventStoreOptionWarnings = options.CheckForLegacyEventStoreConfiguration();
		if (eventStoreOptionWarnings.Any()) {
			log.Warning(
				$"The \"{KurrentConfigurationKeys.LegacyEventStorePrefix}\" configuration root " +
				$"has been deprecated and renamed to \"{KurrentConfigurationKeys.Prefix}\". " +
				"The following settings will still be used, but will stop working in a future release:");
			foreach (var warning in eventStoreOptionWarnings) {
				log.Warning(warning);
			}
		}

		var deprecationWarnings = options.GetDeprecationWarnings();
		if (deprecationWarnings != null) {
			log.Warning($"DEPRECATED{Environment.NewLine}{deprecationWarnings}");
		}
	}

	// only known once the web application builder exists
	public static void WriteHostEnvironment(IHostEnvironment environment) {
		var log = Log;

		log.Information("Environment Name: {0}", environment.EnvironmentName);
		log.Information("ContentRoot Path: {0}", environment.ContentRootPath);
	}
}
