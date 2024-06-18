using System.Runtime.InteropServices;
using McMaster.NETCore.Plugins.LibraryModel;
using Microsoft.Extensions.DependencyModel;
using NativeLibrary = McMaster.NETCore.Plugins.LibraryModel.NativeLibrary;

namespace McMaster.NETCore.Plugins.Loader;

/// <summary>
/// Extensions for configuring a load context using .deps.json files.
/// </summary>
public static class DependencyContextExtensions {
    /// <summary>
    /// Add dependency information to a load context from a .deps.json file.
    /// </summary>
    /// <param name="builder">The builder.</param>
    /// <param name="depsFilePath">The full path to the .deps.json file.</param>
    /// <param name="error">An error, if one occurs while reading .deps.json</param>
    /// <returns>The builder.</returns>
    public static AssemblyLoadContextBuilder TryAddDependencyContext(this AssemblyLoadContextBuilder builder, string depsFilePath, out Exception? error) {
        error = null;
        try {
            builder.AddDependencyContext(depsFilePath);
        }
        catch (Exception ex) {
            error = ex;
        }

        return builder;
    }

    /// <summary>
    /// Add dependency information to a load context from a .deps.json file.
    /// </summary>
    /// <param name="builder">The builder.</param>
    /// <param name="depsFilePath">The full path to the .deps.json file.</param>
    /// <returns>The builder.</returns>
    public static AssemblyLoadContextBuilder AddDependencyContext(this AssemblyLoadContextBuilder builder, string depsFilePath) {
        var reader = new DependencyContextJsonReader();
        using (var file = File.OpenRead(depsFilePath)) {
            var deps = reader.Read(file);
            builder.AddDependencyContext(deps);
        }

        return builder;
    }

    static string GetFallbackRid() {
        string ridBase;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            ridBase = "win10";
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            ridBase = "linux";
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            ridBase = "osx.10.12";
        else
            return "any";

        return RuntimeInformation.OSArchitecture switch {
            Architecture.X86   => ridBase + "-x86",
            Architecture.X64   => ridBase + "-x64",
            Architecture.Arm   => ridBase + "-arm",
            Architecture.Arm64 => ridBase + "-arm64",
            _                  => ridBase
        };
    }

    /// <summary>
    /// Add a pre-parsed <see cref="DependencyContext" /> to the load context.
    /// </summary>
    /// <param name="builder">The builder.</param>
    /// <param name="dependencyContext">The dependency context.</param>
    /// <returns>The builder.</returns>
    public static AssemblyLoadContextBuilder AddDependencyContext(this AssemblyLoadContextBuilder builder, DependencyContext dependencyContext) {
        var ridGraph = dependencyContext.RuntimeGraph.Any() || DependencyContext.Default == null
            ? dependencyContext.RuntimeGraph
            : DependencyContext.Default.RuntimeGraph;

        var rid         = Microsoft.DotNet.PlatformAbstractions.RuntimeEnvironment.GetRuntimeIdentifier();
        var fallbackRid = GetFallbackRid();
        var fallbackGraph = ridGraph.FirstOrDefault(g => g.Runtime == rid)
                         ?? ridGraph.FirstOrDefault(g => g.Runtime == fallbackRid)
                         ?? new RuntimeFallbacks("any");

        foreach (var managed in dependencyContext.ResolveRuntimeAssemblies(fallbackGraph))
            builder.AddManagedLibrary(managed);

        foreach (var library in dependencyContext.ResolveResourceAssemblies())
        foreach (var resource in library.ResourceAssemblies) {
            var resourceDir = Path.GetDirectoryName(Path.GetDirectoryName(resource.Path));

            if (resourceDir != null) {
                var path = Path.Combine(
                    library.Name.ToLowerInvariant(),
                    library.Version,
                    resourceDir
                );

                builder.AddResourceProbingSubpath(path);
            }
        }

        foreach (var native in dependencyContext.ResolveNativeAssets(fallbackGraph))
            builder.AddNativeLibrary(native);

        return builder;
    }

    static IEnumerable<ManagedLibrary> ResolveRuntimeAssemblies(this DependencyContext depContext, RuntimeFallbacks runtimeGraph) {
        var rids = GetRids(runtimeGraph);
        return from library in depContext.RuntimeLibraries
               from assetPath in SelectAssets(rids, library.RuntimeAssemblyGroups)
               select ManagedLibrary.CreateFromPackage(library.Name, library.Version, assetPath);
    }

    static IEnumerable<RuntimeLibrary> ResolveResourceAssemblies(this DependencyContext depContext) =>
        from library in depContext.RuntimeLibraries
        where library.ResourceAssemblies != null && library.ResourceAssemblies.Count > 0
        select library;

    static IEnumerable<NativeLibrary> ResolveNativeAssets(this DependencyContext depContext, RuntimeFallbacks runtimeGraph) {
        var rids = GetRids(runtimeGraph);
        return from library in depContext.RuntimeLibraries
               from assetPath in SelectAssets(rids, library.NativeLibraryGroups)
               where PlatformInformation.NativeLibraryExtensions.Contains(Path.GetExtension(assetPath), StringComparer.OrdinalIgnoreCase)
               select NativeLibrary.CreateFromPackage(library.Name, library.Version, assetPath);
    }

    static IEnumerable<string> GetRids(RuntimeFallbacks? runtimeGraph) {
        return runtimeGraph is null
            ? []
            : new[] { runtimeGraph.Runtime }.Concat(runtimeGraph.Fallbacks.Where(x => x is not null).Cast<string>());
    }

    static IEnumerable<string> SelectAssets(IEnumerable<string> rids, IEnumerable<RuntimeAssetGroup> groups) {
        foreach (var rid in rids) {
            var group = groups.FirstOrDefault(g => g.Runtime == rid);
            if (group != null)
                return group.AssetPaths;
        }

        return groups.GetDefaultAssets();
    }
}