using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using McMaster.NETCore.Plugins.LibraryModel;

namespace McMaster.NETCore.Plugins.Loader;

/// <summary>
/// An implementation of <see cref="AssemblyLoadContext" /> which attempts to load managed and native
/// binaries at runtime immitating some of the behaviors of corehost.
/// </summary>
[DebuggerDisplay("'{Name}' ({_mainAssemblyPath})")]
internal class ManagedLoadContext : AssemblyLoadContext {
    private readonly string                                      _basePath;
    private readonly string                                      _mainAssemblyPath;
    private readonly IReadOnlyDictionary<string, ManagedLibrary> _managedAssemblies;
    private readonly IReadOnlyDictionary<string, NativeLibrary>  _nativeLibraries;
    private readonly IReadOnlyCollection<string>                 _privateAssemblies;
    private readonly ICollection<string>                         _defaultAssemblies;
    private readonly IReadOnlyCollection<string>                 _additionalProbingPaths;
    private readonly bool                                        _preferDefaultLoadContext;
    private readonly string[]                                    _resourceRoots;
    private readonly bool                                        _loadInMemory;
    private readonly bool                                        _lazyLoadReferences;
    private readonly AssemblyLoadContext                         _defaultLoadContext;
    private readonly AssemblyDependencyResolver                  _dependencyResolver;
    private readonly bool                                        _shadowCopyNativeLibraries;
    private readonly string                                      _unmanagedDllShadowCopyDirectoryPath;

    public ManagedLoadContext(
        string mainAssemblyPath,
        IReadOnlyDictionary<string, ManagedLibrary> managedAssemblies,
        IReadOnlyDictionary<string, NativeLibrary> nativeLibraries,
        IReadOnlyCollection<string> privateAssemblies,
        IReadOnlyCollection<string> defaultAssemblies,
        IReadOnlyCollection<string> additionalProbingPaths,
        IReadOnlyCollection<string> resourceProbingPaths,
        AssemblyLoadContext defaultLoadContext,
        bool preferDefaultLoadContext,
        bool lazyLoadReferences,
        bool isCollectible,
        bool loadInMemory,
        bool shadowCopyNativeLibraries
    )
#if FEATURE_UNLOAD
            : base(Path.GetFileNameWithoutExtension(mainAssemblyPath), isCollectible)
#endif
    {
        if (resourceProbingPaths == null) {
            throw new ArgumentNullException(nameof(resourceProbingPaths));
        }

        _mainAssemblyPath = mainAssemblyPath ?? throw new ArgumentNullException(nameof(mainAssemblyPath));

        _dependencyResolver = new AssemblyDependencyResolver(mainAssemblyPath);

        _basePath                 = Path.GetDirectoryName(mainAssemblyPath) ?? throw new ArgumentException(nameof(mainAssemblyPath));
        _managedAssemblies        = managedAssemblies                       ?? throw new ArgumentNullException(nameof(managedAssemblies));
        _privateAssemblies        = privateAssemblies                       ?? throw new ArgumentNullException(nameof(privateAssemblies));
        _defaultAssemblies        = defaultAssemblies != null ? defaultAssemblies.ToList() : throw new ArgumentNullException(nameof(defaultAssemblies));
        _nativeLibraries          = nativeLibraries        ?? throw new ArgumentNullException(nameof(nativeLibraries));
        _additionalProbingPaths   = additionalProbingPaths ?? throw new ArgumentNullException(nameof(additionalProbingPaths));
        _defaultLoadContext       = defaultLoadContext;
        _preferDefaultLoadContext = preferDefaultLoadContext;
        _loadInMemory             = loadInMemory;
        _lazyLoadReferences       = lazyLoadReferences;

        _resourceRoots = new[] { _basePath }
            .Concat(resourceProbingPaths)
            .ToArray();

        _shadowCopyNativeLibraries           = shadowCopyNativeLibraries;
        _unmanagedDllShadowCopyDirectoryPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        if (shadowCopyNativeLibraries) {
            Unloading += _ => OnUnloaded();
        }
    }

    /// <summary>
    /// Load an assembly.
    /// </summary>
    /// <param name="assemblyName"></param>
    /// <returns></returns>
    protected override Assembly? Load(AssemblyName assemblyName) {
        if (assemblyName.Name == null) {
            return null;
        }

        if ((_preferDefaultLoadContext || _defaultAssemblies.Contains(assemblyName.Name)) && !_privateAssemblies.Contains(assemblyName.Name)) {
            try {
                var defaultAssembly = _defaultLoadContext.LoadFromAssemblyName(assemblyName);
                if (defaultAssembly != null) {
                    if (_lazyLoadReferences) {
                        foreach (var reference in defaultAssembly.GetReferencedAssemblies()) {
                            if (reference.Name != null && !_defaultAssemblies.Contains(reference.Name)) {
                                _defaultAssemblies.Add(reference.Name);
                            }
                        }
                    }

                    return defaultAssembly;
                }
            }
            catch { }
        }

        var resolvedPath = _dependencyResolver.ResolveAssemblyToPath(assemblyName);
        if (!string.IsNullOrEmpty(resolvedPath) && File.Exists(resolvedPath)) {
            return LoadAssemblyFromFilePath(resolvedPath);
        }

        if (!string.IsNullOrEmpty(assemblyName.CultureName) && !string.Equals("neutral", assemblyName.CultureName)) {
            foreach (var resourceRoot in _resourceRoots) {
                var resourcePath = Path.Combine(resourceRoot, assemblyName.CultureName, assemblyName.Name + ".dll");
                if (File.Exists(resourcePath)) {
                    return LoadAssemblyFromFilePath(resourcePath);
                }
            }

            return null;
        }

        if (_managedAssemblies.TryGetValue(assemblyName.Name, out var library) && library != null) {
            if (SearchForLibrary(library, out var path) && path != null) {
                return LoadAssemblyFromFilePath(path);
            }
        }
        else {
            var dllName = assemblyName.Name + ".dll";
            foreach (var probingPath in _additionalProbingPaths.Prepend(_basePath)) {
                var localFile = Path.Combine(probingPath, dllName);
                if (File.Exists(localFile)) {
                    return LoadAssemblyFromFilePath(localFile);
                }
            }
        }

        return null;
    }

    public Assembly LoadAssemblyFromFilePath(string path) {
        if (!_loadInMemory) {
            return LoadFromAssemblyPath(path);
        }

        using var file    = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var       pdbPath = Path.ChangeExtension(path, ".pdb");
        if (File.Exists(pdbPath)) {
            using var pdbFile = File.Open(pdbPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return LoadFromStream(file, pdbFile);
        }

        return LoadFromStream(file);
    }

    /// <summary>
    /// Loads the unmanaged binary using configured list of native libraries.
    /// </summary>
    /// <param name="unmanagedDllName"></param>
    /// <returns></returns>
    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName) {
        var resolvedPath = _dependencyResolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        if (!string.IsNullOrEmpty(resolvedPath) && File.Exists(resolvedPath)) {
            return LoadUnmanagedDllFromResolvedPath(resolvedPath, normalizePath: false);
        }

        foreach (var prefix in PlatformInformation.NativeLibraryPrefixes) {
            if (_nativeLibraries.TryGetValue(prefix + unmanagedDllName, out var library)) {
                if (SearchForLibrary(library, prefix, out var path) && path != null) {
                    return LoadUnmanagedDllFromResolvedPath(path);
                }
            }
            else {
                foreach (var suffix in PlatformInformation.NativeLibraryExtensions) {
                    if (!unmanagedDllName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) {
                        continue;
                    }

                    var trimmedName = unmanagedDllName.Substring(0, unmanagedDllName.Length - suffix.Length);

                    if (_nativeLibraries.TryGetValue(prefix + trimmedName, out library)) {
                        if (SearchForLibrary(library, prefix, out var path) && path != null) {
                            return LoadUnmanagedDllFromResolvedPath(path);
                        }
                    }
                    else {
                        var prefixSuffixDllName = prefix + unmanagedDllName + suffix;
                        var prefixDllName       = prefix + unmanagedDllName;

                        foreach (var probingPath in _additionalProbingPaths.Prepend(_basePath)) {
                            var localFile = Path.Combine(probingPath, prefixSuffixDllName);
                            if (File.Exists(localFile)) {
                                return LoadUnmanagedDllFromResolvedPath(localFile);
                            }

                            var localFileWithoutSuffix = Path.Combine(probingPath, prefixDllName);
                            if (File.Exists(localFileWithoutSuffix)) {
                                return LoadUnmanagedDllFromResolvedPath(localFileWithoutSuffix);
                            }
                        }
                    }
                }
            }
        }

        return base.LoadUnmanagedDll(unmanagedDllName);
    }

    private bool SearchForLibrary(ManagedLibrary library, out string? path) {
        var localFile = Path.Combine(_basePath, library.AppLocalPath);
        if (File.Exists(localFile)) {
            path = localFile;
            return true;
        }

        foreach (var searchPath in _additionalProbingPaths) {
            var candidate = Path.Combine(searchPath, library.AdditionalProbingPath);
            if (File.Exists(candidate)) {
                path = candidate;
                return true;
            }
        }

        foreach (var ext in PlatformInformation.ManagedAssemblyExtensions) {
            var local = Path.Combine(_basePath, library.Name.Name + ext);
            if (File.Exists(local)) {
                path = local;
                return true;
            }
        }

        path = null;
        return false;
    }

    private bool SearchForLibrary(NativeLibrary library, string prefix, out string? path) {
        foreach (var ext in PlatformInformation.NativeLibraryExtensions) {
            var candidate = Path.Combine(_basePath, $"{prefix}{library.Name}{ext}");
            if (File.Exists(candidate)) {
                path = candidate;
                return true;
            }
        }

        var local = Path.Combine(_basePath, library.AppLocalPath);
        if (File.Exists(local)) {
            path = local;
            return true;
        }

        foreach (var searchPath in _additionalProbingPaths) {
            var candidate = Path.Combine(searchPath, library.AdditionalProbingPath);
            if (File.Exists(candidate)) {
                path = candidate;
                return true;
            }
        }

        path = null;
        return false;
    }

    private IntPtr LoadUnmanagedDllFromResolvedPath(string unmanagedDllPath, bool normalizePath = true) {
        if (normalizePath) {
            unmanagedDllPath = Path.GetFullPath(unmanagedDllPath);
        }

        return _shadowCopyNativeLibraries
            ? LoadUnmanagedDllFromShadowCopy(unmanagedDllPath)
            : LoadUnmanagedDllFromPath(unmanagedDllPath);
    }

    private IntPtr LoadUnmanagedDllFromShadowCopy(string unmanagedDllPath) {
        var shadowCopyDllPath = CreateShadowCopy(unmanagedDllPath);

        return LoadUnmanagedDllFromPath(shadowCopyDllPath);
    }

    private string CreateShadowCopy(string dllPath) {
        Directory.CreateDirectory(_unmanagedDllShadowCopyDirectoryPath);

        var dllFileName    = Path.GetFileName(dllPath);
        var shadowCopyPath = Path.Combine(_unmanagedDllShadowCopyDirectoryPath, dllFileName);

        if (!File.Exists(shadowCopyPath)) {
            File.Copy(dllPath, shadowCopyPath);
        }

        return shadowCopyPath;
    }

    private void OnUnloaded() {
        if (!_shadowCopyNativeLibraries || !Directory.Exists(_unmanagedDllShadowCopyDirectoryPath)) {
            return;
        }

        try {
            Directory.Delete(_unmanagedDllShadowCopyDirectoryPath, recursive: true);
        }
        catch {
            // ignored
        }
    }
}