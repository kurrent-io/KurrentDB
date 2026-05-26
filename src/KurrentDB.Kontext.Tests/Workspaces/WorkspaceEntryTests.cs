// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Kontext.Workspaces;
using KurrentDB.Kontext.Workspaces.ControlPlane;
using KurrentDB.Kontext.Workspaces.Registry;

namespace KurrentDB.Kontext.Tests.Workspaces;

public class WorkspaceEntryTests {
	static WorkspaceEntry Make(
		bool disableMemory = false,
		bool disableImports = false,
		bool disableInquiries = false,
		bool readOnly = false) =>
		WorkspaceEntry.Create(
			name: "alpha",
			filterRules: [new FilterRule("", null)],
			fullTextIndexingEnabled: true,
			semanticIndexingEnabled: true,
			disableMemory: disableMemory,
			disableImports: disableImports,
			disableInquiries: disableInquiries,
			readOnly: readOnly);

	[Test]
	public async Task Create_Sets_MemoryStreamPrefix_From_Name() {
		var entry = Make();
		await Assert.That(entry.MemoryStreamPrefix).IsEqualTo(WorkspaceNaming.MemoryStreamPrefix("alpha"));
	}

	[Test]
	public async Task Create_Initializes_Status_To_Created() {
		var entry = Make();
		await Assert.That(entry.Status).IsEqualTo(WorkspaceLifecycle.Created);
	}

	[Test]
	public async Task EnsureMemoryReadable_Passes_When_Memory_Enabled() =>
		Make().EnsureMemoryReadable();

	[Test]
	public async Task EnsureMemoryReadable_Throws_When_Memory_Disabled() {
		var entry = Make(disableMemory: true);
		var ex = Assert.Throws<WorkspaceOperationDisabledException>(() => entry.EnsureMemoryReadable());
		await Assert.That(ex.Message).Contains("alpha");
		await Assert.That(ex.Message).Contains("memory read");
	}

	[Test]
	public async Task EnsureMemoryWritable_Passes_When_Memory_Enabled() =>
		Make().EnsureMemoryWritable();

	[Test]
	public async Task EnsureMemoryWritable_Throws_When_Memory_Disabled() {
		var entry = Make(disableMemory: true);
		var ex = Assert.Throws<WorkspaceOperationDisabledException>(() => entry.EnsureMemoryWritable());
		await Assert.That(ex.Message).Contains("memory write");
	}

	[Test]
	public async Task EnsureMemoryWritable_Throws_When_ReadOnly() {
		var entry = Make(readOnly: true);
		var ex = Assert.Throws<WorkspaceOperationDisabledException>(() => entry.EnsureMemoryWritable());
		await Assert.That(ex.Message).Contains("read-only");
	}

	[Test]
	public async Task EnsureMemoryWritable_DisableMemory_Takes_Precedence_Over_ReadOnly() {
		var entry = Make(disableMemory: true, readOnly: true);
		var ex = Assert.Throws<WorkspaceOperationDisabledException>(() => entry.EnsureMemoryWritable());
		// The DisableMemory branch should fire first; the message should not mention read-only.
		await Assert.That(ex.Message).Contains("memory write");
		await Assert.That(ex.Message.Contains("read-only")).IsFalse();
	}

	[Test]
	public async Task EnsureImportable_Passes_When_Imports_Enabled() =>
		Make().EnsureImportable();

	[Test]
	public async Task EnsureImportable_Throws_When_Imports_Disabled() {
		var entry = Make(disableImports: true);
		var ex = Assert.Throws<WorkspaceOperationDisabledException>(() => entry.EnsureImportable());
		await Assert.That(ex.Message).Contains("import");
	}

	[Test]
	public async Task EnsureImportable_Throws_When_ReadOnly() {
		var entry = Make(readOnly: true);
		var ex = Assert.Throws<WorkspaceOperationDisabledException>(() => entry.EnsureImportable());
		await Assert.That(ex.Message).Contains("read-only");
	}

	[Test]
	public async Task EnsureInquiriesEnabled_Passes_When_Inquiries_Enabled() =>
		Make().EnsureInquiriesEnabled();

	[Test]
	public async Task EnsureInquiriesEnabled_Throws_When_Inquiries_Disabled() {
		var entry = Make(disableInquiries: true);
		var ex = Assert.Throws<WorkspaceOperationDisabledException>(() => entry.EnsureInquiriesEnabled());
		await Assert.That(ex.Message).Contains("inquiries");
	}
}