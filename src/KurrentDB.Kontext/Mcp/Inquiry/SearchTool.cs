// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.ComponentModel;
using EventStore.Plugins.Authorization;
using KurrentDB.Kontext.Search;
using KurrentDB.Kontext.Workspaces.Api;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Server;

namespace KurrentDB.Kontext.Mcp.Inquiry;

public sealed record SearchSummary(
	[property: Description("Number of hits new to the working set, for the query at this position.")]
	int New);

public sealed record SearchResult(
	[property: Description("Total events in the inquiry's working set after this call.")]
	int WorkingSetSize,
	[property: Description("Per-query summary, in the order queries were passed.")]
	SearchSummary[] Searches,
	[property: Description("Newly added events, sorted by relevance score descending.")]
	InquiryEvent[] NewEvents);

[McpServerToolType]
public class SearchTool {
	[McpServerTool(Name = "inq_search", UseStructuredContent = true), Description(
		"Hybrid (keyword + semantic) search. Adds new hits to the inquiry's working set; events already in it are skipped. " +
		"Prefer a separate inq_search per distinct question rather than one broad search.")]
	public static async Task<SearchResult> Search(
		IKontextService searchService,
		WorkspaceContext workspaceContext,
		IAuthorizationProvider authz,
		IHttpContextAccessor httpContextAccessor,
		InquiryManager inquiries,
		[Description("The inquiry ID.")] string inquiryId,
		[Description("Google-style keyword queries (max 200 chars each), not full sentences. Prefix a word with ~ to exclude it from results — useful for surfacing events you haven't already seen.")] string[] queries,
		[Description("Natural-language question to rerank results toward.")] string? question = null,
		[Description("Filter to streams matching this pattern. Supports * wildcards (e.g. 'order-*', '*-orders-*', 'tenant-*-2024-*').")] string? streamFilter = null,
		[Description("Filter to event types matching this pattern. Supports * wildcards (e.g. 'Order*', '*Placed', '*Order*').")] string? eventTypeFilter = null) {
		var workspace = workspaceContext.Current;
		var inquiry = inquiries.Get(inquiryId, workspace) ?? throw new InquiryNotFoundException(inquiryId);

		var user = httpContextAccessor.HttpContext!.User;

		var streamAccess = new Dictionary<string, bool>(StringComparer.Ordinal);
		var newEvents = new List<EventResult>();
		var searchSummaries = new List<SearchSummary>();

		foreach (var q in queries) {
			if (string.IsNullOrWhiteSpace(q))
				continue;
			var trimmed = q.Length > 200 ? q[..200] : q;
			var newCount = 0;
			await foreach (var hit in searchService.SearchAsync(workspace, trimmed, query: question,
				streamFilter: streamFilter, eventTypeFilter: eventTypeFilter)) {
				if (!streamAccess.TryGetValue(hit.Stream, out var allowed)) {
					allowed = await authz.CanReadStreamAsync(user, hit.Stream);
					streamAccess[hit.Stream] = allowed;
				}

				var evt = new EventResult {
					Stream = hit.Stream,
					EventNumber = hit.EventNumber,
					CommitPosition = hit.CommitPosition,
					PreparePosition = hit.PreparePosition,
					EventType = hit.EventType,
					Timestamp = hit.Timestamp,
					Data = hit.Data,
					Metadata = hit.Metadata,
					Score = hit.Score,
				};
				if (!allowed)
					evt = evt.Redact();
				if (inquiry.IsForgotten(evt) || inquiry.Contains(evt))
					continue;
				inquiry.Add(evt);
				newEvents.Add(evt);
				newCount++;
			}
			searchSummaries.Add(new SearchSummary(newCount));
		}

		newEvents.Sort((a, b) => (b.Score ?? 0).CompareTo(a.Score ?? 0));
		var sortedNewEvents = new InquiryEvent[newEvents.Count];
		for (var i = 0; i < newEvents.Count; i++)
			sortedNewEvents[i] = InquiryHelpers.ToInquiryEvent(newEvents[i]);

		return new SearchResult(inquiry.WorkingSetSize, searchSummaries.ToArray(), sortedNewEvents);
	}
}