// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.InteropServices;
using DuckDB.NET.Native;
using Kurrent.Quack;

namespace Kurrent.Kontext.Infrastructure.Data;

public abstract class DuckDBExtensionInstaller(string name, string filename) {
 
    public bool Install(DuckDBAdvancedConnection connection) {
        var (sql, installed) = Provision();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
        return installed;
    }
   
    (string Sql, bool Installed, string Path, long Size) Provision() {
        var defaultExtensionPath = DuckDBVendorExtensions.GetDefaultExtensionPath(filename);
        
        if (File.Exists(defaultExtensionPath))
            return ($"LOAD {name};", true, defaultExtensionPath, new FileInfo(defaultExtensionPath).Length);

        if (!File.Exists(filename))
            return ($"INSTALL {name}; LOAD {name};", false, null, 0);
        
        var appExtensionPath = DuckDBVendorExtensions.GetAppExtensionPath(filename);

        if (!File.Exists(appExtensionPath))
            throw new InvalidOperationException($"DuckDB extension not found: {filename}");

        try {
            Directory.CreateDirectory(defaultExtensionPath);

            // Copy-then-rename keeps a concurrent process from observing a half-written binary.
            // the losing racer's rename fails on the now-existing target, and that's fine.
            var destFileName = $"{defaultExtensionPath}.tmp-{Environment.ProcessId}";

            try {
                File.Copy(appExtensionPath, destFileName, overwrite: true);
                File.Move(destFileName, defaultExtensionPath);
            } catch (IOException) when (File.Exists(defaultExtensionPath)) {
                File.Delete(destFileName);
            }

            return ($"LOAD {name};", true);
        }
        catch (Exception ex) {
            throw new InvalidOperationException($"Unable to install DuckDB extension: {name}", ex);
        }
    }
    
    // ReadOnlySpan<byte> Provision() {
    //     var defaultExtensionPath = DuckDBVendorExtensions.GetDefaultExtensionPath(filename);
    //     
    //     if (File.Exists(defaultExtensionPath))
    //         return $"LOAD {name};"u8;
    //
    //     if (!File.Exists(extensionFilename))
    //         return $"INSTALL {name}; LOAD {name};";
    //     
    //     var appExtensionPath = DuckDBVendorExtensions.GetAppExtensionPath(filename);
    //
    //     if (!File.Exists(appExtensionPath))
    //         throw new InvalidOperationException($"DuckDB extension not found: {filename}");
    //
    //     try {
    //         Directory.CreateDirectory(defaultExtensionPath);
    //
    //         // Copy-then-rename keeps a concurrent process from observing a half-written binary.
    //         // the losing racer's rename fails on the now-existing target, and that's fine.
    //         var destFileName = $"{defaultExtensionPath}.tmp-{Environment.ProcessId}";
    //
    //         try {
    //             File.Copy(appExtensionPath, destFileName, overwrite: true);
    //             File.Move(destFileName, defaultExtensionPath);
    //         } catch (IOException) when (File.Exists(defaultExtensionPath)) {
    //             File.Delete(destFileName);
    //         }
    //
    //         return $"LOAD {name};";
    //     }
    //     catch (Exception ex) {
    //         throw new InvalidOperationException($"Unable to install DuckDB extension: {name}", ex);
    //     }
    // }
    
    // The vendored binary is unsigned, and allow_unsigned_extensions is startup-only ("Cannot
    // change allow_unsigned_extensions setting while database is running"), so it must ride the
    // connection string into the engine's open configuration — a SET in bootstrap SQL would
    // throw. Harmless when the loaded build is the signed stock one.
    public static string AmendConnectionString(string connectionString) =>
        connectionString.Contains("allow_unsigned_extensions", StringComparison.OrdinalIgnoreCase)
            ? connectionString
            : $"{connectionString};allow_unsigned_extensions=true";
}

/// <summary>
/// Provisions the vendored DuckDB extension binaries into DuckDB's default extension folder and
/// composes the engine settings and SQL that load them.
/// </summary>
class DuckDBVendorExtensions {
    static readonly string LibraryVersion = 
        NativeMethods.Startup.DuckDBLibraryVersion();

    static readonly string Platform =
        RuntimeInformation.RuntimeIdentifier.ToLowerInvariant()
            .Replace("linux-musl-", "linux-") // Normalize Alpine/Musl to standard linux
            .Replace("win-", "windows-")      // Normalize win to windows
            .Replace("-x64", "_amd64")        // Replace separator and map x64 -> amd64
            .Replace("-x86", "_386")          // Replace separator and map x86 -> 386
            .Replace("-", "_");               // Convert any remaining hyphens to underscores (arm64, s390x, etc.)


    static readonly string UserFolder =
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    
    // DuckDB composes its default extension folder from the engine version and platform;
    // duckdb_library_version() returns the exact version segment (e.g. "v1.5.5").
    static string ExtensionPath(string filename, string baseDirectory) {
        ArgumentException.ThrowIfNullOrWhiteSpace(filename);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
            
        return Path.Combine(
            baseDirectory, "vendor", "duckdb", "extensions",
            LibraryVersion, Platform, filename);
    }
    
    public static string GetDefaultExtensionPath(string filename) =>  ExtensionPath(filename, UserFolder);
    
    public static string GetAppExtensionPath(string filename) =>  ExtensionPath(filename, AppContext.BaseDirectory);
}
