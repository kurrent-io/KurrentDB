using System.Reflection;
using System.Runtime.Loader;

namespace Plugins;

public class ComponentAssemblyLoadContext(string componentAssemblyPath) : AssemblyLoadContext {
    AssemblyDependencyResolver Resolver { get;  } = new(componentAssemblyPath);

    protected override Assembly? Load(AssemblyName assemblyName) {
        var assemblyPath = Resolver.ResolveAssemblyToPath(assemblyName);
        return assemblyPath != null ? LoadFromAssemblyPath(assemblyPath) : null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName) {
        var libraryPath = Resolver.ResolveUnmanagedDllToPath(unmanagedDllName);

        if (libraryPath != null)
            return LoadUnmanagedDllFromPath(libraryPath);

        return IntPtr.Zero;
    }
}