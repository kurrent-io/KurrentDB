// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.ComponentModel;
using KurrentDB.Core;
using KurrentDB.Kontext.Search;
using KurrentDB.Kontext.Workspaces;
using KurrentDB.Kontext.Workspaces.Api;
using KurrentDB.Kontext.Workspaces.Registry;
using ModelContextProtocol.Server;

namespace KurrentDB.Kontext.Mcp.Memory;

public sealed record RecalledFact(
	[property: Description("Topic the fact was retained under.")]
	string Topic,
	[property: Description("The synthesised fact text.")]
	string Fact,
	[property: Description("Source events the fact was synthesised from.")]
	SourceEvent[] SourceEvents,
	[property: Description("UTC timestamp when the fact was retained.")]
	DateTime RetainedAt,
	[property: Description("True if the matched fact was outdated; the current fact is shown here.")]
	bool Superseded,
	[property: Description("Relevance score (0-1).")]
	float Score);

public sealed record RecallResult(
	[property: Description("Number of facts recalled.")]
	int Count,
	[property: Description("The recalled facts, ordered by relevance.")]
	RecalledFact[] Facts);

[McpServerToolType]
public class RecallTool {
	[McpServerTool(Name = "mem_recall", UseStructuredContent = true), Description(
		"Recall facts previously retained in workspace memory. Run before inq_search to surface what you already know; verify against source events when accuracy matters or to gather more context.")]
	public static async Task<RecallResult> Recall(
		IKontextService kontextService,
		ISystemClient client,
		WorkspaceContext workspaceContext,
		WorkspaceRegistry workspaces,
		[Description("Keyword query against retained facts. Prefix a word with ~ to exclude.")] string keywords,
		[Description("Natural-language question to rerank results toward.")] string? question = null) {
		var workspace = workspaceContext.Current;
		if (!workspaces.TryGet(workspace, out var entry))
			throw new WorkspaceNotFoundException(workspace);

		entry.EnsureMemoryReadable();

		var facts = new List<RecalledFact>();
		var seenTopics = new HashSet<string>(StringComparer.Ordinal);

		await foreach (var hit in kontextService.RecallAsync(workspace, keywords, question)) {
			if (!WorkspaceNaming.TryParseMemoryStream(hit.Stream, workspace, out var topic))
				continue;

			if (!seenTopics.Add(topic))
				continue;

			// The index keeps superseded fact versions, so the matched event may be stale.
			// Present the topic's current fact instead, and flag when the matched version was superseded.
			if (await client.Reading.ReadStreamLastEvent(hit.Stream) is not { } latest || latest.OriginalEvent.Data.IsEmpty)
				continue;

			var payload = FactPayload.Parse(latest.OriginalEvent.Data);
			var superseded = hit.EventNumber is { } matched && matched < latest.OriginalEvent.EventNumber;

			facts.Add(new RecalledFact(
				Topic: topic,
				Fact: payload.Fact,
				SourceEvents: payload.SourceEvents,
				RetainedAt: payload.RetainedAt,
				Superseded: superseded,
				Score: MathF.Round(hit.Score, 4)));
		}

		return new RecallResult(facts.Count, facts.ToArray());
	}
}
