// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Kontext.Workspaces.Runtime;
using Microsoft.Extensions.Logging.Abstractions;

namespace KurrentDB.Kontext.Tests.Workspaces;

public class WorkspaceBloomFiltersTests {
	static (WorkspaceBloomFilters Filters, string Dir) Make() {
		var dir = Path.Combine(Path.GetTempPath(), $"kontext-bloom-{Guid.NewGuid():N}");
		return (new WorkspaceBloomFilters(dir, NullLogger<WorkspaceBloomFilters>.Instance), dir);
	}

	[Test]
	public async Task Add_Then_MightContain_Returns_True_For_LogPositions() {
		var (filters, dir) = Make();
		try {
			filters.AddLogPosition(42);
			filters.AddLogPosition(1_000_000);
			await Assert.That(filters.MightContainLogPosition(42)).IsTrue();
			await Assert.That(filters.MightContainLogPosition(1_000_000)).IsTrue();
		} finally { filters.Dispose(); Directory.Delete(dir, recursive: true); }
	}

	[Test]
	public async Task MightContain_Returns_False_For_Unknown_LogPosition() {
		var (filters, dir) = Make();
		try {
			filters.AddLogPosition(42);
			await Assert.That(filters.MightContainLogPosition(99999)).IsFalse();
		} finally { filters.Dispose(); Directory.Delete(dir, recursive: true); }
	}

	[Test]
	public async Task LogPositions_And_StreamHashes_Are_Distinct_Filters() {
		var (filters, dir) = Make();
		try {
			filters.AddLogPosition(42);
			// Same numeric key in a different keyspace should not register.
			await Assert.That(filters.MightContainStreamHash(42)).IsFalse();

			filters.AddStreamHash(99);
			await Assert.That(filters.MightContainLogPosition(99)).IsFalse();
		} finally { filters.Dispose(); Directory.Delete(dir, recursive: true); }
	}

	[Test]
	public async Task State_Persists_Across_Reopen() {
		var dir = Path.Combine(Path.GetTempPath(), $"kontext-bloom-{Guid.NewGuid():N}");
		try {
			using (var filters = new WorkspaceBloomFilters(dir, NullLogger<WorkspaceBloomFilters>.Instance)) {
				filters.AddLogPosition(42);
				filters.AddStreamHash(7);
			} // dispose persists the filters

			using var reopened = new WorkspaceBloomFilters(dir, NullLogger<WorkspaceBloomFilters>.Instance);
			await Assert.That(reopened.MightContainLogPosition(42)).IsTrue();
			await Assert.That(reopened.MightContainStreamHash(7)).IsTrue();
		} finally {
			if (Directory.Exists(dir))
				Directory.Delete(dir, recursive: true);
		}
	}
}
