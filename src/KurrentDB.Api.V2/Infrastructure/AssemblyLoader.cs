using System.Collections.Concurrent;
using System.Reflection;

namespace KurrentDB.Api.Infrastructure;

/// <summary>
/// Utility class for loading assemblies from a specified directory with caching and filtering capabilities.
/// <remarks>
/// Experimental and intended for internal use only.
/// </remarks>
/// </summary>
static class AssemblyLoader {
    static readonly ConcurrentDictionary<string, Assembly?> AssemblyCache = new();

    static readonly HashSet<string> ExcludedAssemblyPrefixes = new(StringComparer.OrdinalIgnoreCase) {
        "System", "Microsoft", "mscorlib", "netstandard"
    };

    public static IEnumerable<Assembly> LoadFrom(string directoryPath, Predicate<string>? assemblyFileNameFilter = null) {
        ArgumentNullException.ThrowIfNull(directoryPath);

        foreach (var assemblyFile in Directory.EnumerateFiles(directoryPath, "*.dll", SearchOption.TopDirectoryOnly)) {
            // Skip if matches any excluded prefix
            var fileName = Path.GetFileName(assemblyFile);
            if (ExcludedAssemblyPrefixes.Any(prefix => fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                continue;

            if (!(assemblyFileNameFilter?.Invoke(Path.GetFileName(assemblyFile)) ?? true))
                continue;

            var assembly = AssemblyCache.GetOrAdd(
                assemblyFile, static filePath => {
                    try {
                        return Assembly.LoadFrom(filePath);
                    }
                    catch (Exception) {
                        return null; // Cache null to avoid repeated failed attempts
                    }
                }
            );

            if (assembly is not null)
                yield return assembly;
        }
    }
}
