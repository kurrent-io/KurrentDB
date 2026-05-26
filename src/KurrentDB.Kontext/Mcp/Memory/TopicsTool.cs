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

public sealed record TopicHit(
	[property: Description("Topic name.")]
	string Topic,
	[property: Description("Relevance score.")]
	float Score,
	[property: Description("The latest retained fact for this topic.")]
	string LatestFact,
	[property: Description("UTC timestamp the latest fact was retained.")]
	DateTime RetainedAt);

public sealed record TopicsResult(
	[property: Description("Matching topics, ordered by relevance.")]
	TopicHit[] Topics);

[McpServerToolType]
public class TopicsTool {
	const int DefaultLimit = 20;

	[McpServerTool(Name = "mem_topics", UseStructuredContent = true), Description(
		"Discover memory topics in the current workspace via hybrid (keyword + semantic) search over topic names. " +
		"Each hit includes the latest fact retained under that topic so you can judge whether it covers what you want to store.")]
	public static async Task<TopicsResult> Topics(
		IKontextService kontextService,
		ISystemClient client,
		WorkspaceContext workspaceContext,
		WorkspaceRegistry workspaces,
		[Description("Keyword query against topic names. Omit to list topics. Prefix a word with ~ to exclude.")] string? keywords = null) {
		var workspace = workspaceContext.Current;
		if (!workspaces.TryGet(workspace, out var entry))
			throw new WorkspaceNotFoundException(workspace);

		entry.EnsureMemoryReadable();

		var hits = new TopicHit[DefaultLimit];
		var count = 0;

		await foreach (var hit in kontextService.DiscoverStreams(workspace, keywords, DefaultLimit, IndexKind.MemoryStreams)) {
			if (!WorkspaceNaming.TryParseMemoryStream(hit.Name, workspace, out var topic))
				continue;

			var fact = "";
			var retainedAt = default(DateTime);
			var latest = await client.Reading.ReadStreamLastEvent(hit.Name);
			if (latest is { } evt && !evt.OriginalEvent.Data.IsEmpty) {
				var payload = FactPayload.Parse(evt.OriginalEvent.Data);
				fact = payload.Fact;
				retainedAt = payload.RetainedAt;
			}

			hits[count++] = new TopicHit(topic, hit.Score, fact, retainedAt);
		}

		return new TopicsResult(count == hits.Length ? hits : hits[..count]);
	}
}
