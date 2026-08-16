// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.InteropServices;
using DuckDB.NET.Native;

namespace Kurrent.Kontext.Infrastructure.Data;

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


    // Where DuckDB itself installs and loads extensions from.
    static readonly string UserFolder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".duckdb");

    // Where the build ships the vendored extensions, mirroring Kurrent.Kontext.csproj.
    static readonly string AppFolder =
        Path.Combine(AppContext.BaseDirectory, "vendor", "duckdb");

    // Both roots use DuckDB's own repository layout, <root>/extensions/<version>/<platform>/,
    // so either can be handed to the engine as an extension_directory. duckdb_library_version()
    // returns the exact version segment (e.g. "v1.5.5").
    static string ExtensionPath(string filename, string baseDirectory) {
        ArgumentException.ThrowIfNullOrWhiteSpace(filename);

        return Path.Combine(baseDirectory, "extensions", LibraryVersion, Platform, filename);
    }

    public static string GetDefaultExtensionPath(string filename) => ExtensionPath(filename, UserFolder);

    public static string GetAppExtensionPath(string filename) => ExtensionPath(filename, AppFolder);

    /// <summary>
    /// Finds the vendored extension shipped beside the application.
    /// </summary>
    /// <remarks>
    /// Only the application declares the vendored build, so it is present next to a deployed node
    /// and absent from a test host. A caller that finds nothing here should fall back to the
    /// engine's own extension repository — at the cost of the stock build, which is not the
    /// vendored one.
    /// </remarks>
    /// <param name="filename">The extension file name.</param>
    /// <param name="path">The full path, when the extension ships with this application.</param>
    /// <returns><see langword="true"/> when the extension is present.</returns>
    public static bool TryGetAppExtensionPath(string filename, out string path) {
        path = GetAppExtensionPath(filename);

        return File.Exists(path);
    }

    /// <summary>
    /// Links the shipped extension into DuckDB's own extension folder so <c>LOAD {name}</c>
    /// resolves it by name, including from an external duckdb session.
    /// </summary>
    /// <remarks>
    /// A symbolic link by default rather than a copy: the binary is ~225 MB, a link crosses volumes
    /// where a hard link cannot, and it keeps resolving to whatever the build last shipped, so
    /// nothing has to compare contents to decide whether to refresh it. Creating one needs
    /// Developer Mode or elevation on Windows, which is what <paramref name="copy"/> is for.
    /// </remarks>
    /// <param name="name">The extension name, as <c>LOAD</c> addresses it.</param>
    /// <param name="filename">The extension file name.</param>
    /// <param name="copy">Copies the file instead of linking to it.</param>
    public static void InstallDevExtension(string name, string filename, bool copy = false) {
        var appExtensionPath = GetAppExtensionPath(filename);

        if (!File.Exists(appExtensionPath))
            throw new InvalidOperationException($"DuckDB extension not found: {filename}");

        var defaultExtensionPath = GetDefaultExtensionPath(filename);

        if (!copy && LinksToShippedExtension(defaultExtensionPath, appExtensionPath))
            return;

        try {
            Directory.CreateDirectory(Path.GetDirectoryName(defaultExtensionPath)!);

            // Clears a stale link or an earlier copy: deleting a symbolic link removes the link
            // and never the file it points at, and deleting a path that does not exist is a no-op.
            File.Delete(defaultExtensionPath);

            if (copy) {
                // Copy-then-rename keeps a concurrent process from observing a half-written binary;
                // the losing racer's rename fails on the now-existing target, which is fine.
                var stagedPath = $"{defaultExtensionPath}.tmp-{Environment.ProcessId}";

                try {
                    File.Copy(appExtensionPath, stagedPath, overwrite: true);
                    File.Move(stagedPath, defaultExtensionPath);
                } catch (IOException) when (File.Exists(defaultExtensionPath)) {
                    File.Delete(stagedPath);
                }
            } else {
                File.CreateSymbolicLink(defaultExtensionPath, appExtensionPath);
            }
        } catch (IOException) when (!copy && LinksToShippedExtension(defaultExtensionPath, appExtensionPath)) {
            // A concurrent process linked it first, which leaves exactly the desired state.
        } catch (Exception ex) {
            throw new InvalidOperationException($"Unable to install DuckDB dev extension: {name}", ex);
        }

        // ResolveLinkTarget throws when the path does not exist and returns null for a real file,
        // so existence is established first and a plain copy answers false.
        static bool LinksToShippedExtension(string path, string shippedPath) =>
            File.Exists(path) && File.ResolveLinkTarget(path, false)?.FullName == Path.GetFullPath(shippedPath);
    }
}