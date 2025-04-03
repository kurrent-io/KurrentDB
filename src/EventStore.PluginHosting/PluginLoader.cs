// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

namespace EventStore.PluginHosting;

public sealed class PluginLoader : IDisposable {
	private readonly DirectoryInfo _rootPluginDirectory;
	private readonly PluginLoadContext[] _contexts;

	private IEnumerable<DirectoryInfo> PluginDirectories {
		get {
			if (!_rootPluginDirectory.Exists) {
				yield break;
			}

			yield return _rootPluginDirectory;
			foreach (var pluginDirectory in _rootPluginDirectory.EnumerateDirectories()) {
				yield return pluginDirectory;
			}
		}
	}

	public PluginLoader(DirectoryInfo rootPluginDirectory, Assembly shared = null) {
		_rootPluginDirectory = rootPluginDirectory ?? throw new ArgumentNullException(nameof(rootPluginDirectory));
		_contexts = PluginDirectories.Select(directory => new PluginLoadContext(directory, shared)).ToArray();
	}

	public IEnumerable<T> Load<T>() where T : class {
		foreach (var loadContext in _contexts)
			foreach (var assembly in loadContext.Assemblies)
				foreach (var pluginType in assembly.GetExportedTypes())
					if (typeof(T).IsAssignableFrom(pluginType) && !pluginType.IsAbstract && !pluginType.IsInterface)
						yield return (T)Activator.CreateInstance(pluginType);
	}

	public void Dispose() {
		foreach (var context in _contexts) {
			context.Unload();
		}
	}

	private class PluginLoadContext : AssemblyLoadContext {
		private static readonly string[] Shared = {"Serilog", "YamlDotNet", "EventStore.Plugins"};
		private readonly Assembly _shared;
		private readonly AssemblyDependencyResolver _resolver;

		public PluginLoadContext(DirectoryInfo directory, Assembly shared) : base(true) {
			_resolver = new AssemblyDependencyResolver(directory.FullName);
			foreach (var library in directory.GetFiles("*.dll")
				.Where(file => !Shared.Contains(Path.GetFileNameWithoutExtension(file.Name)))) {
				try {
					LoadFromAssemblyPath(library.FullName);
				} catch (BadImageFormatException) {
					// We shouldn't be loading this dll. Ignore it
				}
			}
			_shared = shared;
		}

		protected override Assembly Load(AssemblyName assemblyName) {
			if (assemblyName.Name == _shared?.GetName().Name) {
				// dont load the dependency at all, use this one from another context
				return _shared;
			}

			var path = _resolver.ResolveAssemblyToPath(assemblyName);
			if (path != null) {
				// load the dependency into this context
				return LoadFromAssemblyPath(path);
			} else {
				// load the dependency into the default context
				return null;
			}
		}
	}
}
