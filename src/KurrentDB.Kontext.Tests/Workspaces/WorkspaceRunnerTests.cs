// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics.Metrics;
using KurrentDB.Kontext.Workspaces;
using KurrentDB.Kontext.Workspaces.Registry;
using KurrentDB.Kontext.Workspaces.Runtime;
using Microsoft.Extensions.Logging.Abstractions;

namespace KurrentDB.Kontext.Tests.Workspaces;

public class WorkspaceRunnerTests {
	static readonly VectorIndexMetadata Meta = new("Test", "test-model", EmbeddingService.Dimensions);

	static (WorkspaceRunner Runner, StoreRegistry<IFtsStore> Fts, StoreRegistry<IVectorStore> Vec, EmbeddingCache Cache, string Dir)
		Make(string name = "alpha") {
		var dir = Path.Combine(Path.GetTempPath(), $"kontext-runner-{Guid.NewGuid():N}");
		var workspace = WorkspaceEntry.Create(name, [], true, true, false, false, false, false);
		var fts = new StoreRegistry<IFtsStore>();
		var vec = new StoreRegistry<IVectorStore>();
		var cache = new EmbeddingCache(new WorkspaceRegistry(), vec, null!, NullLoggerFactory.Instance);
		var runner = new WorkspaceRunner(
			workspace, dir,
			new KontextEmbeddingsConfig(),
			client: null!, fts, vec, Meta,
			npe: null!, cache, new Meter("test"), () => 0, NullLoggerFactory.Instance);
		return (runner, fts, vec, cache, dir);
	}

	[Test]
	public async Task Mount_Registers_Vector_Stores_Only() {
		var (runner, fts, vec, _, dir) = Make();
		try {
			runner.Mount();

			foreach (var kind in (IndexKind[])[IndexKind.Events, IndexKind.Memory, IndexKind.EventsStreams, IndexKind.MemoryStreams]) {
				// Vector stores are mounted for the cross-workspace cache...
				await Assert.That(vec.TryGet(new WorkspaceIndex("alpha", kind), out _)).IsTrue();
				// ...but FTS stores stay closed — a stopped workspace is never searched.
				await Assert.That(fts.TryGet(new WorkspaceIndex("alpha", kind), out _)).IsFalse();
			}
		} finally {
			await runner.DisposeAsync();
			if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
		}
	}

	[Test]
	public async Task Stop_Unmounts_Fts_But_Keeps_Vector_Mounted() {
		var (runner, fts, vec, _, dir) = Make();
		try {
			// Start mounts both FTS and vector stores (client is null, so the subscription
			// loops fault and retry in the background — Stop cancels them promptly).
			runner.Start();
			await Assert.That(fts.TryGet(new WorkspaceIndex("alpha", IndexKind.Events), out _)).IsTrue();
			await Assert.That(vec.TryGet(new WorkspaceIndex("alpha", IndexKind.Events), out _)).IsTrue();

			await runner.StopAsync();

			// FTS freed (a stopped workspace is never searched); vector kept for the cache.
			await Assert.That(fts.TryGet(new WorkspaceIndex("alpha", IndexKind.Events), out _)).IsFalse();
			await Assert.That(vec.TryGet(new WorkspaceIndex("alpha", IndexKind.Events), out _)).IsTrue();
		} finally {
			await runner.DisposeAsync();
			if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
		}
	}

	[Test]
	public async Task Mount_Is_Idempotent() {
		var (runner, _, vec, _, dir) = Make();
		try {
			runner.Mount();
			vec.TryGet(new WorkspaceIndex("alpha", IndexKind.Events), out var first);
			runner.Mount(); // second call must not replace the mounted store
			vec.TryGet(new WorkspaceIndex("alpha", IndexKind.Events), out var second);
			await Assert.That(second).IsSameReferenceAs(first!);
		} finally {
			await runner.DisposeAsync();
			if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
		}
	}

	[Test]
	public async Task Dispose_Unmounts_Stores() {
		var (runner, _, vec, _, dir) = Make();
		try {
			runner.Mount();
			await Assert.That(vec.TryGet(new WorkspaceIndex("alpha", IndexKind.Events), out _)).IsTrue();

			await runner.DisposeAsync();
			await Assert.That(vec.TryGet(new WorkspaceIndex("alpha", IndexKind.Events), out _)).IsFalse();
		} finally {
			if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
		}
	}

	[Test]
	public async Task Mount_After_Dispose_Throws() {
		var (runner, _, _, _, dir) = Make();
		try {
			await runner.DisposeAsync();
			await Assert.That(() => runner.Mount()).Throws<ObjectDisposedException>();
		} finally {
			if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
		}
	}
}
