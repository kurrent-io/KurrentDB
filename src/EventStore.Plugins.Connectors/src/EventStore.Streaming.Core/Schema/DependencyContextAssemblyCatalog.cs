using System.Reflection;
using Microsoft.Extensions.DependencyModel;

namespace EventStore.Streaming.Schema;

[PublicAPI]
sealed class DependencyContextAssemblyCatalog {
    // ReSharper disable once MemberInitializerValueIgnored
    DependencyContext DependencyContext { get; } = null!;

    public DependencyContextAssemblyCatalog()
        : this(Assembly.GetEntryAssembly(), Assembly.GetCallingAssembly(), Assembly.GetExecutingAssembly()) { }

    DependencyContextAssemblyCatalog(params Assembly?[] assemblies) {
        DependencyContext = assemblies
	        .AsParallel()
            .Where(assembly => assembly is not null)
            .Aggregate(
                DependencyContext.Default!,
                (ctx, assembly) => {
                    var loadedContext = DependencyContext.Load(assembly!);
                    return loadedContext is not null ? ctx.Merge(loadedContext) : ctx;
                }
            );
    }

    public IReadOnlyCollection<Assembly> LoadAssemblies() {
        var results = new HashSet<Assembly> {
            typeof(DependencyContextAssemblyCatalog).Assembly
        };

        var assemblyNames = DependencyContext.RuntimeLibraries
            .SelectMany(library => library.GetDefaultAssemblyNames(DependencyContext));

        foreach (var assemblyName in assemblyNames)
            if (TryLoadAssembly(assemblyName, out var assembly))
                results.Add(assembly!);

        return results;
    }

    static bool TryLoadAssembly(AssemblyName assemblyName, out Assembly? assembly) {
        try {
            return (assembly = Assembly.Load(assemblyName)) is not null;
        }
        catch (Exception) {
            return (assembly = null) is not null;
        }
    }
}

static class AssemblyScanner {
	static readonly Lazy<IReadOnlyCollection<Assembly>> LazyAssemblies = new(() => new DependencyContextAssemblyCatalog().LoadAssemblies());
	
	public static ParallelQuery<Type> ScanTypes(bool includeInternalTypes = true) {
		return LazyAssemblies.Value
			.AsParallel()
			.SelectMany(x => includeInternalTypes ? x.GetTypes() : x.GetExportedTypes());
	}
}