using System.Collections.Concurrent;
using System.Reflection;

namespace KurrentDB.Api.Infrastructure;

static class AssemblyLoader {
    static readonly ConcurrentDictionary<string, Assembly?> AssemblyCache = new();

    static readonly HashSet<string> ExcludedAssemblyPrefixes = new(StringComparer.OrdinalIgnoreCase) {
        "System", "Microsoft", "mscorlib", "netstandard"
    };

    public static IEnumerable<Assembly> LoadFrom(string directoryPath, Predicate<string>? assemblyFileNameFilter = null) {
        ArgumentNullException.ThrowIfNull(directoryPath);
        foreach (var assemblyFile in Directory.EnumerateFiles(directoryPath, "*.dll", SearchOption.TopDirectoryOnly).AsParallel()) {
            // Skip if matches any excluded prefix
            if (ExcludedAssemblyPrefixes.Any(prefix => assemblyFile.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                continue;

            if (!(assemblyFileNameFilter?.Invoke(Path.GetFileName(assemblyFile)) ?? true))
                continue;

            var assembly = AssemblyCache.GetOrAdd(assemblyFile, static filePath => {
                try {
                    return Assembly.LoadFrom(filePath);
                }
                catch (Exception) {
                    return null; // Cache null to avoid repeated failed attempts
                }
            });

            if (assembly is not null)
                yield return assembly;
        }
    }
}
