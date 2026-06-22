// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Kontext.Workspaces;

namespace KurrentDB.Kontext.Tests.Workspaces;

public class WorkspaceNamingTests {
	[Test]
	public async Task IsDefault_True_For_Default_Name() =>
		await Assert.That(WorkspaceNaming.IsDefault(WorkspaceNaming.DefaultName)).IsTrue();

	[Test]
	public async Task IsDefault_False_For_Other_Names() =>
		await Assert.That(WorkspaceNaming.IsDefault("other")).IsFalse();

	[Test]
	public async Task IsDefault_Is_Case_Sensitive() =>
		await Assert.That(WorkspaceNaming.IsDefault("Default")).IsFalse();

	[Test]
	public async Task ManagementStreamName_Has_Category_Prefix() =>
		await Assert.That(WorkspaceNaming.ManagementStreamName("alpha"))
			.IsEqualTo("$kontext-workspaces:alpha");

	[Test]
	public async Task MemoryStreamPrefix_Includes_Workspace() =>
		await Assert.That(WorkspaceNaming.MemoryStreamPrefix("alpha"))
			.IsEqualTo("$kontext-memory:alpha:");

	[Test]
	public async Task MemoryStreamName_Joins_Workspace_And_Topic() =>
		await Assert.That(WorkspaceNaming.MemoryStreamName("alpha", "user-prefs"))
			.IsEqualTo("$kontext-memory:alpha:user-prefs");

	[Test]
	public async Task TryParseMemoryStream_Extracts_Topic() {
		var ok = WorkspaceNaming.TryParseMemoryStream("$kontext-memory:alpha:user-prefs", "alpha", out var topic);
		await Assert.That(ok).IsTrue();
		await Assert.That(topic).IsEqualTo("user-prefs");
	}

	[Test]
	public async Task TryParseMemoryStream_Other_Workspace_Returns_False() {
		var ok = WorkspaceNaming.TryParseMemoryStream("$kontext-memory:other:x", "alpha", out var topic);
		await Assert.That(ok).IsFalse();
		await Assert.That(topic).IsEqualTo("");
	}

	[Test]
	public async Task TryParseMemoryStream_Empty_Topic_Returns_False() {
		// Prefix matches but nothing follows.
		var ok = WorkspaceNaming.TryParseMemoryStream("$kontext-memory:alpha:", "alpha", out var topic);
		await Assert.That(ok).IsFalse();
		await Assert.That(topic).IsEqualTo("");
	}

	[Test]
	public async Task TryParseMemoryStream_Non_Memory_Stream_Returns_False() {
		var ok = WorkspaceNaming.TryParseMemoryStream("orders-1", "alpha", out var topic);
		await Assert.That(ok).IsFalse();
		await Assert.That(topic).IsEqualTo("");
	}

	[Test]
	[Arguments(IndexKind.Events, "events")]
	[Arguments(IndexKind.Memory, "memory")]
	[Arguments(IndexKind.EventsStreams, "events.streams")]
	[Arguments(IndexKind.MemoryStreams, "memory.streams")]
	public async Task PathSegment_Maps_Index_Kind(IndexKind kind, string expected) =>
		await Assert.That(kind.PathSegment()).IsEqualTo(expected);
}