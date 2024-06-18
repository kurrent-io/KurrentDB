using System.Reflection;
using McMaster.NETCore.Plugins;
using Microsoft.Extensions.DependencyModel;

namespace Plugins;

[PublicAPI]
sealed class DependencyContextAssemblyCatalog {
    static readonly string PluginFrameworkAssemblyName;

    public static readonly DependencyContextAssemblyCatalog Default = new();

    static DependencyContextAssemblyCatalog() => PluginFrameworkAssemblyName = typeof(IPlugin).Assembly.GetName().Name!;

    // ReSharper disable once MemberInitializerValueIgnored
    DependencyContext DependencyContext { get; } = null!;

    public DependencyContextAssemblyCatalog()
        : this(Assembly.GetEntryAssembly(), Assembly.GetCallingAssembly(), Assembly.GetExecutingAssembly()) { }

    public DependencyContextAssemblyCatalog(params Assembly?[] assemblies) {
        DependencyContext = assemblies
            .Where(assembly => assembly is not null)
            .Aggregate(
                DependencyContext.Default!,
                (ctx, assembly) => {
                    var loadedContext = DependencyContext.Load(assembly!);
                    return loadedContext is not null ? ctx.Merge(loadedContext) : ctx;
                }
            );
    }

    public IReadOnlyCollection<Assembly> GetAssemblies() {
        var results = new HashSet<Assembly> {
            // typeof(DependencyContextAssemblyCatalog).Assembly
        };

        var assemblyNames23 = DependencyContext.CompileLibraries;

        var assemblyNames = DependencyContext.RuntimeLibraries
            //.Where(x => !IsReferencingGrpcValidation(x) && !IsReferencingFluentValidation(x))
            .SelectMany(library => library.GetDefaultAssemblyNames(DependencyContext))
            .ToList();

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

    // static bool IsReferencingGrpcValidation(Library library) =>
    //     library.Dependencies.Any(dependency => dependency.Name.Equals(GrpcValidationAssemblyName));
    //
    // static bool IsReferencingFluentValidation(Library library) =>
    //     library.Dependencies.Any(dependency => dependency.Name.Equals(FluentValidationAssemblyName));
}