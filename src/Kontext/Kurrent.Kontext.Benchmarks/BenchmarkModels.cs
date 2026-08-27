// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.CompilerServices;

namespace Benchmarks;

/// <summary>
/// Where the models only these benchmarks use are kept. Nothing in Kontext runs on them, so they are
/// downloaded by the leg that needs them rather than by KurrentDB.Kontext.Models on every build — a
/// comparison nobody runs costs nothing.
/// </summary>
static class BenchmarkModels {
	// [CallerFilePath] resolves this project's directory at compile time, so the cache sits beside the
	// source wherever the repo lives rather than under the build output, which a clean would wipe.
	static string ProjectDir([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;

	/// <summary>The cache root, which is also the base an <c>OnnxModelRegistry</c> resolves keys under.</summary>
	public static string Root => Path.Combine(ProjectDir(), ".models");

	public static string Directory(string key) => Path.Combine(Root, key);
}
