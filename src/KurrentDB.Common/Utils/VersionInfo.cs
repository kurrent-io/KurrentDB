// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace KurrentDB.Common.Utils;

public static class VersionInfo {
	public const string DefaultVersion = "default_version";
	public const string OldVersion = "old_version";
	public const string UnknownVersion = "unknown_version";
	private const string VersionPropertiesFileName = "version.properties";

	public static string BuildId { get; } = "";
	public static string Edition { get; } = "";
	public static string VersionPrefix { get; }
	public static string VersionSuffix { get; }
	public static string Version => string.IsNullOrWhiteSpace(VersionSuffix)
		? VersionPrefix
		: VersionPrefix + "-" + VersionSuffix;

	public static string CommitSha { get; } = ThisAssembly.Git.Commit;
	public static string Timestamp { get; } = ThisAssembly.Git.CommitDate;

	public static string Text => $"KurrentDB version {Version} {Edition} ({BuildId}/{CommitSha})";

	static VersionInfo() {
		// the official release assemblies contain the version prefix (4 part number)
		// but not the suffix (beta, rc1, rtm, etc) so that the same assembly can be promoted.
		var versionPrefix = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? string.Empty;
		if (versionPrefix.EndsWith(".0"))
			versionPrefix = versionPrefix[..^2];
		VersionPrefix = versionPrefix;

		var versionFilePath = Path.Join(
			Path.GetDirectoryName(AppContext.BaseDirectory),
			VersionPropertiesFileName
		);

		// Fall back to the copy embedded into the assembly when the file is absent on disk
		// (e.g. in tests, AppContext.BaseDirectory is `bin/` instead of `bin/<tfm>/`, or single-file deployments).
		using var reader = File.Exists(versionFilePath)
			? new StreamReader(versionFilePath)
			: OpenEmbeddedProperties();

		var properties = reader is null
			? FrozenDictionary<string, string>.Empty
			: LoadProperties(reader);

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

	private static StreamReader OpenEmbeddedProperties() =>
		Assembly.GetExecutingAssembly().GetManifestResourceStream(VersionPropertiesFileName) is { } stream
			? new StreamReader(stream)
			: null;

	private static IReadOnlyDictionary<string, string> LoadProperties(TextReader reader) {
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
