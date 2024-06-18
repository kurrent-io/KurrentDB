using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace McMaster.NETCore.Plugins.Loader;

/// <summary>
/// Extensions for creating a load context using settings from a runtimeconfig.json file
/// </summary>
public static class RuntimeConfigExtensions {
    const string JsonExt = ".json";

    static readonly JsonSerializerOptions s_serializerOptions = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Adds additional probing paths to a managed load context using settings found in the runtimeconfig.json
    /// and runtimeconfig.dev.json files.
    /// </summary>
    /// <param name="builder">The context builder</param>
    /// <param name="runtimeConfigPath">The path to the runtimeconfig.json file</param>
    /// <param name="includeDevConfig">Also read runtimeconfig.dev.json file, if present.</param>
    /// <param name="error">The error, if one occurs while parsing runtimeconfig.json</param>
    /// <returns>The builder.</returns>
    public static AssemblyLoadContextBuilder TryAddAdditionalProbingPathFromRuntimeConfig(
        this AssemblyLoadContextBuilder builder,
        string runtimeConfigPath,
        bool includeDevConfig,
        out Exception? error
    ) {
        error = null;
        try {
            var config = TryReadConfig(runtimeConfigPath);
            if (config == null)
                return builder;

            RuntimeConfig? devConfig = null;
            if (includeDevConfig) {
                var configDevPath = runtimeConfigPath.Substring(0, runtimeConfigPath.Length - JsonExt.Length) + ".dev.json";
                devConfig = TryReadConfig(configDevPath);
            }

            var tfm = config.runtimeOptions?.Tfm ?? devConfig?.runtimeOptions?.Tfm;

            if (config.runtimeOptions != null)
                AddProbingPaths(builder, config.runtimeOptions, tfm);

            if (devConfig?.runtimeOptions != null)
                AddProbingPaths(builder, devConfig.runtimeOptions, tfm);

            if (tfm != null) {
                var dotnet = Process.GetCurrentProcess().MainModule!.FileName;
                if (string.Equals(Path.GetFileNameWithoutExtension(dotnet), "dotnet", StringComparison.OrdinalIgnoreCase)) {
                    var dotnetHome = Path.GetDirectoryName(dotnet);
                    if (dotnetHome != null)
                        builder.AddProbingPath(Path.Combine(dotnetHome, "store", RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant(), tfm));
                }
            }
        }
        catch (Exception ex) {
            error = ex;
        }

        return builder;
    }

    static void AddProbingPaths(AssemblyLoadContextBuilder builder, RuntimeOptions options, string? tfm) {
        if (options.AdditionalProbingPaths == null)
            return;

        foreach (var item in options.AdditionalProbingPaths) {
            var path = item;
            if (path.Contains("|arch|"))
                path = path.Replace("|arch|", RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant());

            if (path.Contains("|tfm|")) {
                if (tfm == null)
                    continue;

                path = path.Replace("|tfm|", tfm);
            }

            builder.AddProbingPath(path);
        }
    }

    static RuntimeConfig? TryReadConfig(string path) {
        try {
            var file = File.ReadAllBytes(path);
            return JsonSerializer.Deserialize<RuntimeConfig>(file, s_serializerOptions);
        }
        catch {
            return null;
        }
    }
}