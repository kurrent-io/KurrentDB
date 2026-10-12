// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;

namespace KurrentDB.Embedded.Tests;

/// <summary>
/// The package bundles the assemblies of a whole project closure and declares the external packages they
/// need; <c>AddClosurePackageReferences</c> collects that declaration from the closure's package
/// references. Anything the collection leaves out is still referenced by the IL, and the consumer finds
/// out at runtime, the first time that code path runs, rather than when they compile.
/// </summary>
/// <remarks>
/// The package references are not the evidence here — they are what produced the declaration, so checking
/// them against it proves nothing. What each assembly references is recorded in its own metadata, and that
/// is what this reads.
/// </remarks>
public class BundledAssemblyReferencesTests {
	/// <summary>
	/// References the collection drops on purpose, each with the reason it is safe to drop. An entry
	/// belongs here only when nothing loads the assembly at runtime; if something does, the package is
	/// missing a dependency and the answer is to declare it, not to list it here.
	/// </summary>
	static readonly Dictionary<string, string> Permitted = new(StringComparer.OrdinalIgnoreCase);

	[Test]
	public async Task every_reference_of_a_bundled_assembly_is_declared() {
		var restore = RestoreOutput.Load();

		var provided = new HashSet<string>(restore.BundledAssemblies, StringComparer.OrdinalIgnoreCase);
		provided.UnionWith(restore.DeclaredAssemblies);
		provided.UnionWith(FrameworkAssemblies());

		// assembly -> what it references that nothing provides
		var undeclared = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
		var unreadable = new SortedSet<string>(StringComparer.Ordinal);

		foreach (var assembly in restore.BundledAssemblies) {
			var path = Path.Combine(AppContext.BaseDirectory, assembly + ".dll");
			if (!File.Exists(path)) {
				unreadable.Add(assembly);
				continue;
			}

			foreach (var reference in ReferencesOf(path)) {
				if (provided.Contains(reference) || Permitted.ContainsKey(reference))
					continue;

				if (!undeclared.TryGetValue(reference, out var referrers))
					undeclared[reference] = referrers = new SortedSet<string>(StringComparer.Ordinal);
				referrers.Add(assembly);
			}
		}

		// Every bundled assembly is a project reference of this test project too, so a missing file means
		// the closure this reads and the one that gets packed have come apart.
		unreadable.ShouldBeEmpty($"bundled assemblies not found next to the tests: {string.Join(", ", unreadable)}");

		undeclared.ShouldBeEmpty(
			"the package bundles assemblies that reference packages it does not declare, which fails for a "
			+ "consumer at runtime rather than at compile time:"
			+ string.Concat(undeclared.Select(x => $"{Environment.NewLine}  {x.Key} <- {string.Join(", ", x.Value)}")));

		await Task.CompletedTask;
	}

	static IEnumerable<string> ReferencesOf(string path) {
		using var file = File.OpenRead(path);
		using var pe = new PEReader(file);

		if (!pe.HasMetadata)
			yield break;

		var metadata = pe.GetMetadataReader();
		foreach (var handle in metadata.AssemblyReferences)
			yield return metadata.GetString(metadata.GetAssemblyReference(handle).Name);
	}

	/// <summary>
	/// The shared framework a consumer gets for free. Every other entry on the trusted platform assembly
	/// list came from this test project's own references, which is a far larger set than the package
	/// declares, so they are told apart by where they are loaded from.
	/// </summary>
	static IEnumerable<string> FrameworkAssemblies() {
		var application = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
		var trusted = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "";

		return trusted
			.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
			.Where(x => !string.Equals(Path.GetDirectoryName(x), application, StringComparison.OrdinalIgnoreCase))
			.Select(Path.GetFileNameWithoutExtension)
			.OfType<string>();
	}
}

/// <summary>
/// What restoring KurrentDB.Embedded resolved: the project assemblies the package bundles, and the
/// assemblies of the packages it declares as dependencies.
/// </summary>
file sealed record RestoreOutput(IReadOnlyCollection<string> BundledAssemblies, IReadOnlyCollection<string> DeclaredAssemblies) {
	public static RestoreOutput Load() {
		var path = typeof(BundledAssemblyReferencesTests).Assembly
			.GetCustomAttributes<AssemblyMetadataAttribute>()
			.Single(x => x.Key == "EmbeddedProjectAssetsFile")
			.Value ?? throw new InvalidOperationException("EmbeddedProjectAssetsFile has no value.");

		if (!File.Exists(path))
			throw new InvalidOperationException($"KurrentDB.Embedded has not been restored: no {path}.");

		using var assets = JsonDocument.Parse(File.ReadAllBytes(path));

		// One target framework, and everything restore resolved against it. Most of that is there because
		// some project in the closure asked for it, which says nothing about the package: pack writes the
		// nuspec from this project's own references, so those are the ones to start from.
		var root = assets.RootElement;
		var target = root.GetProperty("targets").EnumerateObject().Single().Value;

		var libraries = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
		foreach (var library in target.EnumerateObject())
			libraries[library.Name.Split('/')[0]] = library.Value;

		// The project being restored does not appear in its own targets, and the package ships it too.
		List<string> bundled = [root.GetProperty("project").GetProperty("restore")
			.GetProperty("projectName").GetString()!];

		foreach (var library in libraries.Values)
			if (TypeOf(library) == "project")
				bundled.AddRange(AssembliesOf(library));

		// A dependency the project reference suppresses (PrivateAssets=all) is one pack leaves out of the
		// package, so it provides nothing here either.
		var roots = root
			.GetProperty("project").GetProperty("frameworks").EnumerateObject().Single().Value
			.GetProperty("dependencies").EnumerateObject()
			.Where(x => !(x.Value.TryGetProperty("suppressParent", out var suppress)
						  && suppress.GetString() == "All"))
			.Select(x => x.Name);

		// A consumer restores what the package declares and gets the dependencies of those packages with
		// it, so the package provides that whole graph — and nothing beside it, however much else the
		// closure dragged into this restore.
		List<string> declared = [];
		var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var pending = new Stack<string>(roots);

		while (pending.TryPop(out var id)) {
			if (!visited.Add(id) || !libraries.TryGetValue(id, out var library) || TypeOf(library) != "package")
				continue;

			declared.AddRange(AssembliesOf(library));

			if (library.TryGetProperty("dependencies", out var dependencies))
				foreach (var dependency in dependencies.EnumerateObject())
					pending.Push(dependency.Name);
		}

		return new RestoreOutput(bundled, declared);
	}

	static string? TypeOf(JsonElement library) =>
		library.TryGetProperty("type", out var kind) ? kind.GetString() : null;

	/// <summary>
	/// The assembly names a resolved library contributes. Compile and runtime are both read because a
	/// package can provide one without the other, and "_._" is the placeholder for neither.
	/// </summary>
	static IEnumerable<string> AssembliesOf(JsonElement library) =>
		new[] { "compile", "runtime" }
			.Where(x => library.TryGetProperty(x, out _))
			.SelectMany(x => library.GetProperty(x).EnumerateObject())
			.Where(x => !x.Name.EndsWith("_._", StringComparison.Ordinal))
			.Select(x => Path.GetFileNameWithoutExtension(x.Name))
			.Where(x => x.Length > 0);
}
