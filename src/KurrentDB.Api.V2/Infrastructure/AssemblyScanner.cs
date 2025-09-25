using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;

namespace KurrentDB.Api.Infrastructure;

/// <summary>
/// Provides utilities for scanning assemblies to discover and analyse types.
/// </summary>
class AssemblyScanner {
    public static readonly AssemblyScanner System = new(AssemblyLoader.LoadFrom(AppDomain.CurrentDomain.BaseDirectory), includeInternalTypes: true);

    public static AssemblyScanner UsingAssembliesFrom(string directoryPath, Predicate<string>? filter = null, bool includeInternalTypes = false) =>
        new(AssemblyLoader.LoadFrom(directoryPath, filter), includeInternalTypes);

    public static AssemblyScanner UsingDefaultAssemblies(Predicate<string>? filter = null, bool includeInternalTypes = false) =>
        new(AssemblyLoader.LoadFrom(AppDomain.CurrentDomain.BaseDirectory, filter), includeInternalTypes);

    public static AssemblyScanner UsingAssemblies(IEnumerable<Assembly?> assemblies, bool includeInternalTypes = false) =>
        new(assemblies.Where(x => x is not null).Cast<Assembly>(), includeInternalTypes);

    AssemblyScanner(IEnumerable<Assembly> assemblies, bool includeInternalTypes)  {
        LazyAssemblies = new(assemblies.ToArray);
        LazyTypes      = new(() => LoadAllTypes(LazyAssemblies.Value, includeInternalTypes));
    }

    Lazy<Assembly[]> LazyAssemblies { get; }
    Lazy<Type[]>     LazyTypes      { get; }

    public Assembly[] Assemblies => LazyAssemblies.Value;
    public Type[]     Types      => LazyTypes.Value;

    public ParallelQuery<Type> Scan() => Types.AsParallel();

    static Type[] LoadAllTypes(Assembly[] assemblies, bool includeInternalTypes = true) {
        return assemblies
            .AsParallel()
            .SelectMany(ass => GetAllTypes(ass, includeInternalTypes))
            .Distinct()
            .ToArray();

        static IEnumerable<Type> GetAllTypes(Assembly assembly, bool includeInternalTypes) {
            try {
                return includeInternalTypes ? assembly.GetTypes() : assembly.GetExportedTypes();
            }
            catch (ReflectionTypeLoadException ex) when (ex.Types is not null) {
                return ex.Types.Where(type => type is not null).Cast<Type>();
            }
        }
    }
}

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

static class ParallelQueryTypeFilters {
    // public static ParallelQuery<Type> InstancesOf(this ParallelQuery<Type> query, Type type) {
    //     Debug.Assert(!type.IsMissing(), "Type.Missing should not be passed to InstancesOf");
    //     return query.Where(t => t is { IsClass: true, IsAbstract: false } & t.IsAssignableTo(type));
    // }

    // public static ParallelQuery<Type> InstancesOf<T>(this ParallelQuery<Type> query) =>
    //     query.InstancesOf(typeof(T));
    //
    // public static ParallelQuery<Type> InNamespace(this ParallelQuery<Type> query, string? namespacePrefix) =>
    //     !string.IsNullOrWhiteSpace(namespacePrefix)
    //         ? query.Where(type => type.IsInNamespace(namespacePrefix))
    //         : query;



    // [DebuggerStepThrough]
    // internal static bool IsInstantiableClass(this Type type) =>
    //     type is { IsClass: true, IsAbstract: false };

    // [DebuggerStepThrough]
    // internal static bool IsInNamespace(this Type type, string namespacePrefix) =>
    //     type.Namespace?.StartsWith(namespacePrefix, StringComparison.Ordinal) ?? false;
}
