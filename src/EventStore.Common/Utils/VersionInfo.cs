// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;

namespace EventStore.Common.Utils;

public static class VersionInfo {
	public const string DefaultVersion = "default_version";
	public const string OldVersion = "old_version";
	public const string UnknownVersion = "unknown_version";

	public static string BuildId { get; private set; } = "";
	public static string Edition { get; private set; } = "";
	public static string VersionPrefix { get; private set; } = "";
	public static string VersionSuffix { get; private set; } = "";
	public static string Version => string.IsNullOrWhiteSpace(VersionSuffix)
		? VersionPrefix
		: VersionPrefix + "-" + VersionSuffix;

	public static string CommitSha { get; private set; } = ThisAssembly.Git.Commit;
	public static string Timestamp { get; private set; } = ThisAssembly.Git.CommitDate;

	public static string Text => $"KurrentDB version {Version} {Edition} ({BuildId}/{CommitSha})";

	static VersionInfo() {
		// the official release assemblies contain the version prefix (4 part number)
		// but not the suffix (beta, rc1, rtm, etc) so that the same assembly can be promoted.
		var versionPrefix = Assembly.GetEntryAssembly().GetName().Version.ToString();
		if (versionPrefix.EndsWith(".0"))
			versionPrefix = versionPrefix[..^2];
		VersionPrefix = versionPrefix;

		var versionFilePath = Path.Join(
			Path.GetDirectoryName(AppDomain.CurrentDomain.BaseDirectory),
			"version.properties");
		var properties = LoadProperties(versionFilePath);

		if (properties.TryGetValue("version_suffix", out var versionSuffix))
			VersionSuffix = versionSuffix;

		if (properties.TryGetValue("commit_sha", out var commitSha))
			CommitSha = commitSha;

		if (properties.TryGetValue("timestamp", out var timestamp))
			Timestamp = timestamp;

		if (properties.TryGetValue("build_id", out var buildId))
			BuildId = buildId;

		if (properties.TryGetValue("edition", out var edition))
			Edition = edition;
	}

	private static Dictionary<string, string> LoadProperties(string file) {
		using var reader = new StreamReader(file);

		var properties = new Dictionary<string, string>();
		string line;
		while ((line = reader.ReadLine()) != null) {
			var parts = line.Split('=', 2);
			if (parts.Length == 2)
				properties[parts[0]] = parts[1];
		}

		return properties;
	}
}
