// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.InteropServices;
using DuckDB.NET.Native;

namespace Kurrent.Kontext.Infrastructure.Data;

/// <summary>
/// Provisions the vendored DuckDB extension binaries into DuckDB's default extension folder and
/// composes the engine settings and SQL that load them.
/// </summary>
static class VendoredDuckDBExtensions {
    /// <summary>The vendored lance build deployed next to the application by the project's copy step.</summary>
    public static string LanceExtensionPath { get; } =
        Path.Combine(AppContext.BaseDirectory, "vendor", "duckdb", "extensions", "lance.duckdb_extension");

    static readonly Lazy<string> ProvisionedLoadSql = new(ProvisionLance);

    /// <summary>
    /// Ensures a lance build is available to name-based <c>LOAD</c> and returns the bootstrap
    /// statement to run on every connection.
    /// </summary>
    /// <remarks>
    /// Three tiers, resolved once per process: an extension already present in DuckDB's default
    /// folder wins — the environment owns it and is never second-guessed; otherwise the vendored
    /// binary is copied there; with nothing vendored the returned statement falls back to
    /// <c>INSTALL lance;</c> so development works without a vendored build.
    /// </remarks>
    public static string EnsureLanceInstalled() => ProvisionedLoadSql.Value;

    // The vendored binary is unsigned, and allow_unsigned_extensions is startup-only ("Cannot
    // change allow_unsigned_extensions setting while database is running"), so it must ride the
    // connection string into the engine's open configuration — a SET in bootstrap SQL would
    // throw. Harmless when the loaded build is the signed stock one.
    public static string AmendConnectionString(string connectionString) =>
        connectionString.Contains("allow_unsigned_extensions", StringComparison.OrdinalIgnoreCase)
            ? connectionString
            : $"{connectionString};allow_unsigned_extensions=true";

    static string ProvisionLance() {
        // DuckDB composes its default extension folder from the engine version and platform;
        // duckdb_library_version() returns the exact version segment (e.g. "v1.5.5").
        var target = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".duckdb", "extensions",
            NativeMethods.Startup.DuckDBLibraryVersion(), PlatformName(),
            "lance.duckdb_extension");

        if (File.Exists(target))
            return "LOAD lance;";

        if (!File.Exists(LanceExtensionPath))
            return "INSTALL lance; LOAD lance;";

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);

        // Copy-then-rename keeps a concurrent process from observing a half-written binary; the
        // losing racer's rename fails on the now-existing target, which is the desired state.
        var temp = $"{target}.tmp-{Environment.ProcessId}";

        try {
            File.Copy(LanceExtensionPath, temp, overwrite: true);
            File.Move(temp, target);
        } catch (IOException) when (File.Exists(target)) {
            File.Delete(temp);
        }

        return "LOAD lance;";
    }

    static string PlatformName() =>
        RuntimeInformation.RuntimeIdentifier.ToLowerInvariant()
            .Replace("linux-musl-", "linux-") // Normalize Alpine/Musl to standard linux
            .Replace("win-", "windows-")      // Normalize win to windows
            .Replace("-x64", "_amd64")        // Replace separator and map x64 -> amd64
            .Replace("-x86", "_386")          // Replace separator and map x86 -> 386
            .Replace("-", "_");               // Convert any remaining hyphens to underscores (arm64, s390x, etc.)
}
