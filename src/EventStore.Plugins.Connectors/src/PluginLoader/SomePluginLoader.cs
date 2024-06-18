using System.Data;
using System.Reflection;
using System.Runtime.Loader;
using McMaster.NETCore.Plugins;
using Microsoft.Extensions.Logging.Abstractions;

namespace Plugins;

public class SomePluginLoader : AssemblyLoadContext {
    public SomePluginLoader(ILoggerFactory? loggerFactory = null) : base(true) =>
        Logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<SomePluginLoader>();

    ILogger Logger { get; }

    public (Assembly Assembly, IPlugin Plugin)? LoadPluginAssemblies(string assemblyDirectory) {
        (Assembly Assembly, IPlugin Plugin)? assemblyGroup = null;

        // load the plugins with their dependencies
        foreach (var assemblyPath in Directory.GetFiles(assemblyDirectory, "*.dll")) {
            // if PluginBase is present, skip loading
            if (Path.GetFileName(assemblyPath).StartsWith("McMaster")) {
                Logger.LogWarning(
                    "Coral.PluginBase assembly detected, please remove from plugin folder. " +
                    $"Skipping load of: {assemblyPath}"                                      +
                    " to ensure plug-in can load."
                );

                continue;
            }

            var assembly = LoadFromAssemblyPath(assemblyPath);
            try {
                var types = assembly.GetTypes();
                // if assembly has more than 1 plugin,
                // throw exception about poor design.
                var pluginCount = types.Count(t => typeof(IPlugin).IsAssignableFrom(t));

                if (pluginCount > 1)
                    throw new ConstraintException(
                        "Cannot load assembly with more than 1 plugin." +
                        " Please separate your plugins into multiple assemblies"
                    );

                // if assembly has no plugins, continue, it's a needed dependency
                if (pluginCount == 0) {
                    Logger.LogDebug("Loaded plugin dependency: {assemblyName}", assembly.GetName().Name);
                    continue;
                }

                var pluginType = types.Single(t => typeof(IPlugin).IsAssignableFrom(t));
                var plugin     = Activator.CreateInstance(pluginType) as IPlugin;

                if (plugin != null) {
                    Logger.LogInformation("Loaded plugin: {name}", plugin.Name);
                    assemblyGroup = (assembly, plugin);
                }
            }
            catch (ReflectionTypeLoadException) {
                Logger.LogWarning("Exception thrown loading types for assembly: {AssemblyName}", assembly.GetName().Name);
            }
        }

        return assemblyGroup;
    }
}

public class RagingPluginLoader : AssemblyLoadContext {
    public RagingPluginLoader(ILoggerFactory? loggerFactory = null) : base(true) =>
        Logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<SomePluginLoader>();

    ILogger Logger { get; }

    public (Assembly Assembly, IPlugin Plugin)? LoadPluginAssemblies(string assemblyDirectory) {
        (Assembly Assembly, IPlugin Plugin)? assemblyGroup = null;

        // load the plugins with their dependencies
        foreach (var assemblyPath in Directory.GetFiles(assemblyDirectory, "*.dll")) {
            // if PluginBase is present, skip loading
            if (Path.GetFileName(assemblyPath).StartsWith("McMaster")) {
                Logger.LogWarning(
                    "Coral.PluginBase assembly detected, please remove from plugin folder. " +
                    $"Skipping load of: {assemblyPath}"                                      +
                    " to ensure plug-in can load."
                );

                continue;
            }

            var assembly = LoadFromAssemblyPath(assemblyPath);
            try {
                var types = assembly.GetTypes();
                // if assembly has more than 1 plugin,
                // throw exception about poor design.
                var pluginCount = types.Count(t => typeof(IPlugin).IsAssignableFrom(t));

                if (pluginCount > 1)
                    throw new ConstraintException(
                        "Cannot load assembly with more than 1 plugin." +
                        " Please separate your plugins into multiple assemblies"
                    );

                // if assembly has no plugins, continue, it's a needed dependency
                if (pluginCount == 0) {
                    Logger.LogDebug("Loaded plugin dependency: {assemblyName}", assembly.GetName().Name);
                    continue;
                }

                var pluginType = types.Single(t => typeof(IPlugin).IsAssignableFrom(t));
                var plugin     = Activator.CreateInstance(pluginType) as IPlugin;

                if (plugin != null) {
                    Logger.LogInformation("Loaded plugin: {name}", plugin.Name);
                    assemblyGroup = (assembly, plugin);
                }
            }
            catch (ReflectionTypeLoadException) {
                Logger.LogWarning("Exception thrown loading types for assembly: {AssemblyName}", assembly.GetName().Name);
            }
        }

        return assemblyGroup;
    }

    public List<Assembly> Load(string assemblyPath) {
        var resolver = new AssemblyDependencyResolver(assemblyPath);

        //resolver.ResolveAssemblyToPath()

        List<Assembly> assemblyGroup = [];

        var assembly = LoadFromAssemblyPath(assemblyPath);

        try {
            var types = assembly.GetTypes();
            // if assembly has more than 1 plugin,
            // throw exception about poor design.
            var pluginCount = types.Count(t => typeof(IPlugin).IsAssignableFrom(t) && t.IsClass);

            if (pluginCount == 0)
                throw new ConstraintException("Plugin assembly must contain at least 1 plugin.");

            var pluginType = types.Single(t => typeof(IPlugin).IsAssignableFrom(t) && t.IsClass);

            var friends = assembly.GetReferencedAssemblies();

            var temp = assembly.CreateInstance(pluginType.FullName!);

            // var plugin     = Activator.CreateInstance(pluginType) as IPlugin;

            // if (plugin != null) {
            //     Logger.LogInformation("Loaded plugin: {name}", plugin.Name);
            //     assemblyGroup = (assembly, plugin);
            // }
        }
        catch (ReflectionTypeLoadException) {
            Logger.LogWarning("Exception thrown loading types for assembly: {AssemblyName}", assembly.GetName().Name);
        }

        return assemblyGroup;
    }
}