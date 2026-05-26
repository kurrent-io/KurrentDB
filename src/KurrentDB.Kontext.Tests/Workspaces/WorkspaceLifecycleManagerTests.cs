// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics.Metrics;
using KurrentDB.Kontext.Workspaces.Runtime;
using Microsoft.Extensions.Logging.Abstractions;

namespace KurrentDB.Kontext.Tests.Workspaces;

/// <summary>
/// Tests for the lifecycle manager's invariants that don't require actually spawning
/// the full runner (Lucene indexes, embedding pipeline, etc.). Start() needs the full
/// stack so it's covered by IntegrationTests in the plugin project.
/// </summary>
public class WorkspaceLifecycleManagerTests {
	static WorkspaceLifecycleManager Make(out string root) {
		root = Path.Combine(Path.GetTempPath(), $"kontext-lifecycle-{Guid.NewGuid():N}");
		var storage = new KontextStorageConfig { DataPath = root };
		return new WorkspaceLifecycleManager(
			storage,
			embeddingsConfig: null!,
			client: null!,
			ftsStores: null!,
			vectorStores: null!,
			vectorMeta: null!,
			npe: null!,
			embeddingCache: null!,
			meter: new Meter("test"),
			writerCheckpoint: () => 0,
			loggerFactory: NullLoggerFactory.Instance);
	}

	[Test]
	public async Task Stop_Throws_When_Workspace_Not_Running() {
		var manager = Make(out _);
		await Assert.That(async () => await manager.Stop("ghost"))
			.Throws<InvalidOperationException>()
			.WithMessageContaining("not running");
	}

	[Test]
	public async Task Remove_Does_Not_Throw_When_Workspace_Not_Running() {
		var manager = Make(out _);
		// Remove of a workspace that was never started should be a no-op (no throw).
		await manager.Remove("ghost");
	}

	[Test]
	public async Task Remove_Deletes_Workspace_Directory() {
		var manager = Make(out var root);
		var workspaceDir = Path.Combine(root, "workspaces", "alpha");
		Directory.CreateDirectory(workspaceDir);
		File.WriteAllText(Path.Combine(workspaceDir, "checkpoint.fts"), "marker");

		await manager.Remove("alpha");

		await Assert.That(Directory.Exists(workspaceDir)).IsFalse();
	}

	[Test]
	public async Task Remove_Of_Missing_Directory_Does_Not_Throw() {
		var manager = Make(out _);
		await manager.Remove("ghost");
		// No directory existed; should silently succeed.
	}

	[Test]
	public async Task StartAsync_Is_NoOp() {
		var manager = Make(out _);
		await manager.StartAsync(CancellationToken.None);
	}

	[Test]
	public async Task StopAsync_With_No_Runners_Is_NoOp() {
		var manager = Make(out _);
		await manager.StopAsync(CancellationToken.None);
	}
}