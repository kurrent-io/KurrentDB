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
/// <remarks>
/// Shared so that a report from an embedded host carries the same facts as one from the server, and so that
/// a requirement added here is enforced by both. A node starting is a lifecycle event, so the report is
/// Information wherever it runs; a host that finds it chatty can filter on this class's source context.
/// </remarks>
public static class NodePreflight {
	// resolved per call rather than captured, because a host may configure Serilog after this type is
	// first touched
	static ILogger Log => Serilog.Log.ForContext(typeof(NodePreflight));

	/// <summary>
	/// Describes what was built, what it is running on and how it is configured, and reports whether a node
	/// can run here at all. Values marked sensitive are masked.
	/// </summary>
	/// <returns>
	/// True if a node can start. Otherwise false, with <paramref name="error"/> saying why — which is the
	/// caller's to report, because the server executable logs it and exits while an embedded database
	/// throws it at a caller who may never have configured our logger.
	/// </returns>
	public static bool TryPrepare(ClusterVNodeOptions options, [NotNullWhen(false)] out string? error) {
		var log = Log;

		// first, and before anything is described: the chunk and index files are memory mapped, and the
		// address space of a 32-bit process does not stretch to a database of any size
		if (!Environment.Is64BitProcess) {
			error = "KurrentDB requires a 64-bit process to run.";
			return false;
		}

		// Before the banner, and louder: an option that was not recognised would otherwise look as though
		// it had been applied.
		var unknownLevel = options.Application.AllowUnknownOptions ? LogEventLevel.Warning : LogEventLevel.Fatal;
		foreach (var (option, suggestion) in options.Unknown.Options) {
			if (string.IsNullOrEmpty(suggestion))
				log.Write(unknownLevel, "The option {option} is not a known option.", option);
			else
				log.Write(unknownLevel, "The option {option} is not a known option. Did you mean {suggestion}?", option, suggestion);
		}

		log.Information("{description,-25} {version} {edition} ({buildId}/{commitSha}, {timestamp})", "DB VERSION:",
			VersionInfo.Version, VersionInfo.Edition, VersionInfo.BuildId, VersionInfo.CommitSha, VersionInfo.Timestamp);
		log.Information("{description,-25} {osArchitecture} ", "OS ARCHITECTURE:",
			System.Runtime.InteropServices.RuntimeInformation.OSArchitecture);
		log.Information("{description,-25} {osFlavor} ({osVersion})", "OS:",
			RuntimeInformation.OsPlatform, Environment.OSVersion);
		log.Information("{description,-25} {osRuntimeVersion} ({architecture}-bit)", "RUNTIME:",
			RuntimeInformation.RuntimeVersion, RuntimeInformation.RuntimeMode);
		log.Information("{description,-25} {maxGeneration} IsServerGC: {isServerGC} Latency Mode: {latencyMode}", "GC:",
			GC.MaxGeneration == 0 ? "NON-GENERATION (PROBABLY BOEHM)" : $"{GC.MaxGeneration + 1} GENERATIONS",
			GCSettings.IsServerGC,
			GCSettings.LatencyMode);

		// reported even by an embedded database, which writes no log files of its own: the setting is
		// still what the logs endpoint and the disk metrics are looking at
		log.Information("{description,-25} {logsDirectory}", "LOGS:", options.Logging.Log);
		log.Information("{description,-25} {isWindowsService}", "IsWindowsService:", WindowsServiceHelpers.IsWindowsService());

		var gcSettings = string.Join($"{Environment.NewLine}    ",
			GC.GetConfigurationVariables().Select(kvp => $"{kvp.Key}: {kvp.Value}"));
		log.Information($"GC Configuration settings:{Environment.NewLine}    {{settings}}", gcSettings);

		log.Information(options.DumpOptions()!);

		WriteConfigurationWarnings(log, options);

		// after the banner, which has just named each one and guessed at what was meant
		if (options.UnknownOptionsDetected && !options.Application.AllowUnknownOptions) {
			error = "Found unknown options. To continue anyway, set " +
				$"{nameof(ClusterVNodeOptions.ApplicationOptions.AllowUnknownOptions)} to true.";
			return false;
		}

		error = null;
		return true;
	}

	/// <summary>
	/// Settings that still work but will not forever: a legacy file location, the old configuration root,
	/// or an option that has been superseded.
	/// </summary>
	/// <remarks>
	/// Said once, as the node describes itself, so that whoever reads the banner reads these alongside the
	/// configuration they are about. An embedded host hears them too: a deprecated setting passed through
	/// <c>Settings</c> is just as deprecated as one in a configuration file.
	/// </remarks>
	static void WriteConfigurationWarnings(ILogger log, ClusterVNodeOptions options) {
		foreach (var locationWarning in options.CheckForLegacyDefaultLocations(LocationOptionWithLegacyDefault.SupportedLegacyLocations))
			log.Warning(locationWarning);

		var legacyRootWarnings = options.CheckForLegacyEventStoreConfiguration();
		if (legacyRootWarnings.Length > 0) {
			log.Warning(
				$"The \"{KurrentConfigurationKeys.LegacyEventStorePrefix}\" configuration root " +
				$"has been deprecated and renamed to \"{KurrentConfigurationKeys.Prefix}\". " +
				"The following settings will still be used, but will stop working in a future release:");
			foreach (var warning in legacyRootWarnings)
				log.Warning(warning);
		}

		if (options.GetDeprecationWarnings() is { } deprecationWarnings)
			log.Warning($"DEPRECATED{Environment.NewLine}{deprecationWarnings}");
	}

	/// <summary>
	/// The web host the node is being wired into, which is only known once the builder exists.
	/// </summary>
	/// <remarks>
	/// Both matter more to an embedded database than to the server, because both are the host
	/// application's: the environment name comes from its <c>ASPNETCORE_ENVIRONMENT</c> and decides
	/// whether gRPC reports detailed errors, and the content root is where the admin UI's static assets
	/// are looked for.
	/// </remarks>
	public static void WriteHostEnvironment(IHostEnvironment environment) {
		var log = Log;

		log.Information("Environment Name: {environmentName}", environment.EnvironmentName);
		log.Information("ContentRoot Path: {contentRootPath}", environment.ContentRootPath);
	}
}
