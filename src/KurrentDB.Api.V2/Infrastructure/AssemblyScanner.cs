// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Reflection;

namespace KurrentDB.Api.Infrastructure;

/// <summary>
/// Provides utilities for scanning assemblies to discover and analyse types.
/// <remarks>
/// Experimental and intended for internal use only.
/// </remarks>
/// </summary>
class AssemblyScanner {
    public static AssemblyScanner UsingAssemblies(IEnumerable<Assembly?> assemblies, bool includeInternalTypes = false) =>
        new(assemblies.Where(x => x is not null).Cast<Assembly>(), includeInternalTypes);

    public static AssemblyScanner UsingAssembly(Assembly assembly, bool includeInternalTypes = false) =>
        new([assembly], includeInternalTypes);

    public static AssemblyScanner UsingExecutingAssembly(bool includeInternalTypes = false) =>
        UsingAssembly(Assembly.GetExecutingAssembly(), includeInternalTypes);

    AssemblyScanner(IEnumerable<Assembly> assemblies, bool includeInternalTypes)  {
        LazyAssemblies = new(assemblies.ToArray);
        LazyTypes      = new(() => LoadAllTypes(LazyAssemblies.Value, includeInternalTypes));
    }

    Lazy<Assembly[]> LazyAssemblies { get; }
    Lazy<Type[]>     LazyTypes      { get; }

    public Assembly[] Assemblies => LazyAssemblies.Value;

    public ParallelQuery<Type> Scan() => LazyTypes.Value.AsParallel();

    static Type[] LoadAllTypes(Assembly[] assemblies, bool includeInternalTypes = true) {
        return assemblies
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
