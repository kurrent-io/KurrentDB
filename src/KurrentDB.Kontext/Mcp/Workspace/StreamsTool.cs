// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.ComponentModel;
using KurrentDB.Kontext.Search;
using KurrentDB.Kontext.Workspaces.Api;
using ModelContextProtocol.Server;

namespace KurrentDB.Kontext.Mcp.Workspace;

public sealed record StreamsResult(
	[property: Description("Matching streams, ordered by relevance.")]
	StreamHit[] Streams);

[McpServerToolType]
public class StreamsTool {
	const int DefaultLimit = 50;

	[McpServerTool(Name = "ws_streams", UseStructuredContent = true), Description(
		"Discover streams in the current workspace via hybrid (keyword + semantic) search over stream names.")]
	public static async Task<StreamsResult> Streams(
		IKontextService kontextService,
		WorkspaceContext workspaceContext,
		[Description("Keyword query against stream names. Omit to list streams. Prefix a word with ~ to exclude.")] string? keywords = null) {
		var hits = new StreamHit[DefaultLimit];
		var count = 0;
		await foreach (var hit in kontextService.DiscoverStreams(workspaceContext.Current, keywords, DefaultLimit))
			hits[count++] = hit;
		return new StreamsResult(count == hits.Length ? hits : hits[..count]);
	}
}
