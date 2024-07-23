using System.Reflection;
using System.Runtime.Loader;
using DotNext.Collections.Generic;
using EventStore.Connect.Connectors;
using EventStore.Streaming;
using EventStore.Streaming.Connectors.Sinks;

namespace PluginLoaderLab;

// [PublicAPI]
// public class AssemblyScanner {
//     public AssemblyScanner(params Assembly[] assemblies) => Assemblies = assemblies;
//
//     IReadOnlyCollection<Assembly> Assemblies { get; }
//
//     public ParallelQuery<Type> Scan(bool includeInternalTypes = false, bool throwOnError = false) {
//         return Assemblies
//             .AsParallel()
//             .SelectMany(x => includeInternalTypes ? GetTypes(x, throwOnError) : x.GetExportedTypes());
//
//         static IEnumerable<Type> GetTypes(Assembly assembly, bool throwOnLoadError) {
//             try {
//                 return assembly.GetTypes();
//             }
//             catch (ReflectionTypeLoadException ex) {
//                 if (throwOnLoadError)
//                     throw;
//
//                 return ex.Types.Where(type => type is not null).Cast<Type>();
//             }
//         }
//     }
//
//     public ParallelQuery<Type> ScanClasses<T>(bool includeInternalTypes = false, bool throwOnError = false) =>
//         Scan(includeInternalTypes, throwOnError)
//             .Where(type => type.IsAssignableTo(typeof(T)) && type.IsInstantiableClass());
// }

// public static class AssemblyScannerExtensions {
//     public static List<Type> SinkConnectorTypes(this AssemblyScanner scanner) =>
//         scanner.ScanClasses<ISink>().ToList();
//
//     public static List<Type> SinkValidatorTypes(this AssemblyScanner scanner) =>
//         scanner.ScanClasses<IConnectorValidator>().Where(x => x != typeof(ConnectorsValidation)).ToList();
// }

public sealed class ConnectorsCatalog : IDisposable {
    ConnectorsCatalog(ConnectorLoadContext[] contexts) {
        Contexts   = contexts;
        Assemblies = Contexts.SelectMany(x => x.Assemblies).OrderBy(x => x.FullName).ToArray();
    }

    ConnectorsCatalog(IEnumerable<DirectoryInfo> directories)
        : this(directories.Select(directory => new ConnectorLoadContext(directory)).ToArray()) { }

    ConnectorLoadContext[] Contexts { get; }

    public Assembly[] Assemblies { get; private set; }

    public List<Type> ScanSinks(bool includeInternalTypes = false) =>
        ScanClasses<ISink>(includeInternalTypes).ToList();

    public List<Type> ScanSinkValidators(bool includeInternalTypes = false) =>
        ScanClasses<IConnectorValidator>(includeInternalTypes).Where(x => x != typeof(ConnectorsValidation)).ToList();

    public void Dispose() {
        Assemblies = [];
        Contexts.ForEach(x => x.Unload());
    }

    public static ConnectorsCatalog From(DirectoryInfo connectorsDirectory) =>
        new ConnectorsCatalog(connectorsDirectory.EnumerateDirectories());

    public static ConnectorsCatalog From(string connectorsDirectoryPath) =>
        From(new DirectoryInfo(connectorsDirectoryPath));

    public static ConnectorsCatalog FromAppDirectory() =>
        From(AppContext.BaseDirectory);

    class ConnectorLoadContext : AssemblyLoadContext {
        readonly AssemblyDependencyResolver _resolver;

        public ConnectorLoadContext(DirectoryInfo directory) : base(true) {
            _resolver = new AssemblyDependencyResolver(directory.FullName);
            foreach (var library in directory.GetFiles("*.dll"))
                LoadFromAssemblyPath(library.FullName);
        }

        protected override Assembly? Load(AssemblyName assemblyName) {
            var path = _resolver.ResolveAssemblyToPath(assemblyName);
            return path is not null ? LoadFromAssemblyPath(path) : null;
        }
    }

    ParallelQuery<Type> Scan(bool includeInternalTypes = false, bool throwOnError = false) {
        return Assemblies
            .AsParallel()
            .SelectMany(x => includeInternalTypes ? GetTypes(x, throwOnError) : x.GetExportedTypes());

        static IEnumerable<Type> GetTypes(Assembly assembly, bool throwOnLoadError) {
            try {
                return assembly.GetTypes();
            } catch (ReflectionTypeLoadException ex) when (!throwOnLoadError) {
                return ex.Types.Where(type => type is not null).Cast<Type>();
            }
        }
    }

    ParallelQuery<Type> ScanClasses<T>(bool includeInternalTypes = false, bool throwOnError = false) =>
        Scan(includeInternalTypes, throwOnError)
            .Where(type => type.IsAssignableTo(typeof(T)) && type.IsInstantiableClass());
}