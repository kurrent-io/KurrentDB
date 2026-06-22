// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.ComponentModel;
using System.Text.Json;
using KurrentDB.Core;
using KurrentDB.Kontext.Workspaces;
using KurrentDB.Kontext.Workspaces.Api;
using KurrentDB.Kontext.Workspaces.Registry;
using ModelContextProtocol.Server;

namespace KurrentDB.Kontext.Mcp.Memory;

public sealed record RetainedFact {
	[Description(
		"Topic this fact belongs to. Allowed characters: letters, digits and '-'. " +
		"Each topic holds a single canonical fact — retaining to the same topic overwrites it. " +
		"Use fine-grained kebab-case topics like 'alice-current-employer', 'order-123-status', 'q3-revenue-target'. " +
		"Call mem_topics first to discover existing topics — reuse one only when updating an existing fact.")]
	public string Topic { get; init; } = "";

	[Description("A complete, self-contained fact (max 200 chars) with explicit entity names and time references.")]
	public string Fact { get; init; } = "";

	[Description("Keywords NOT already present in the fact text, chosen to match hypothetical future recall queries.")]
	public string[] Keywords { get; init; } = [];

	[Description("Source events this fact was synthesised from.")]
	public SourceEvent[] SourceEvents { get; init; } = [];
}

public sealed record RetainResult(
	[property: Description("Number of facts retained.")]
	int Retained);

[McpServerToolType]
public class RetainTool {
	public const string EventType = "FactRetained";
	public const int MaxFactLength = 200;

	[McpServerTool(Name = "mem_retain", UseStructuredContent = true), Description(
		"Retain synthesised facts in workspace memory, one fact per topic. " +
		"Call mem_topics first — if a topic already exists for this fact, reuse it (retaining to the same topic overwrites the existing fact).")]
	public static async Task<RetainResult> Retain(
		ISystemClient client,
		WorkspaceContext workspaceContext,
		WorkspaceRegistry workspaces,
		[Description("Facts to retain.")] RetainedFact[] facts) {
		var workspace = workspaceContext.Current;
		if (!workspaces.TryGet(workspace, out var entry))
			throw new WorkspaceNotFoundException(workspace);

		entry.EnsureMemoryWritable();

		for (var i = 0; i < facts.Length; i++) {
			var f = facts[i];
			if (!IsValidTopic(f.Topic))
				throw new ToolInputException($"Fact #{i}: invalid topic '{f.Topic}'. Allowed characters: letters, digits, '-'.");
			if (string.IsNullOrWhiteSpace(f.Fact))
				throw new ToolInputException($"Fact #{i} (topic '{f.Topic}'): fact text is empty.");
			if (f.Fact.Length > MaxFactLength)
				throw new ToolInputException($"Fact #{i} (topic '{f.Topic}'): fact exceeds max length of {MaxFactLength} chars.");
		}

		foreach (var f in facts) {
			var data = JsonSerializer.SerializeToUtf8Bytes(
				new FactPayload(f.Fact, f.Keywords, f.SourceEvents, DateTime.UtcNow),
				FactPayload.JsonOptions);
			await client.WriteAsync(WorkspaceNaming.MemoryStreamName(workspace, f.Topic), EventType, data);
		}

		return new RetainResult(facts.Length);
	}

	static bool IsValidTopic(string topic) {
		if (string.IsNullOrEmpty(topic))
			return false;
		foreach (var c in topic)
			if (!(char.IsLetterOrDigit(c) || c is '-'))
				return false;
		return true;
	}
}