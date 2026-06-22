// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.ComponentModel;
using EventStore.Plugins.Authorization;
using KurrentDB.Core;
using KurrentDB.Kontext.Indexing;
using KurrentDB.Kontext.Workspaces;
using KurrentDB.Kontext.Workspaces.Api;
using KurrentDB.Kontext.Workspaces.Registry;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Server;

namespace KurrentDB.Kontext.Mcp.Inquiry;

public sealed record ReadResult(
	[property: Description("Total events in the inquiry's working set after this call.")]
	int WorkingSetSize,
	[property: Description("True when there are no more events to read past the returned range. Stop reading once this is true.")]
	bool EndOfStream,
	[property: Description("Newly added events (events already in the working set or previously forgotten are silently omitted).")]
	InquiryEvent[] NewEvents);

[McpServerToolType]
public class ReadTool {
	[McpServerTool(Name = "inq_read", UseStructuredContent = true), Description(
		"Read events from a stream into the inquiry's working set (count ≤ 32 per call). Iterate until EndOfStream is true; already-seen or forgotten events are skipped.")]
	public static async Task<ReadResult> Read(
		ISystemClient client,
		WorkspaceContext workspaceContext,
		WorkspaceRegistry workspaces,
		IAuthorizationProvider authz,
		IHttpContextAccessor httpContextAccessor,
		InquiryManager inquiries,
		[Description("The inquiry ID.")] string inquiryId,
		[Description("Event or range to read from a single stream.")] EventRef eventRef) {
		var inquiry = inquiries.Get(inquiryId, workspaceContext.Current) ?? throw new InquiryNotFoundException(inquiryId);

		if (InquiryHelpers.ValidateRange(eventRef) is { } error)
			throw new ToolInputException(error);

		if (!workspaces.TryGet(workspaceContext.Current, out var entry))
			throw new WorkspaceNotFoundException(workspaceContext.Current);

		if (!entry.IndexesStream(eventRef.Stream))
			throw new StreamAccessDeniedException(eventRef.Stream);

		var user = httpContextAccessor.HttpContext!.User;

		if (!await authz.CanReadStreamAsync(user, eventRef.Stream))
			throw new StreamAccessDeniedException(eventRef.Stream);

		using var filterLease = entry.FilterPool.Rent();
		var filter = filterLease.Filter;

		var newEvents = new InquiryEvent[eventRef.Count];
		var newCount = 0;
		var totalRead = 0;
		await foreach (var resolved in client.ReadAsync(eventRef.Stream, eventRef.EventNumber, eventRef.Count)) {
			totalRead++;

			if (filter.Evaluate(resolved.OriginalEvent, NullLogger.Instance) != FilterResult.Match)
				continue;

			var evt = resolved.ToEventResult(eventRef.Stream);
			if (inquiry.IsForgotten(evt) || inquiry.Contains(evt))
				continue;
			inquiry.Add(evt);
			newEvents[newCount++] = InquiryHelpers.ToInquiryEvent(evt);
		}

		return new ReadResult(
			inquiry.WorkingSetSize,
			EndOfStream: totalRead < eventRef.Count,
			newCount == newEvents.Length ? newEvents : newEvents[..newCount]);
	}
}
