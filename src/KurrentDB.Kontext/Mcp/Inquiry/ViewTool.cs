// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.ComponentModel;
using KurrentDB.Kontext.Workspaces.Api;
using ModelContextProtocol.Server;

namespace KurrentDB.Kontext.Mcp.Inquiry;

public sealed record ViewResult(
	[property: Description("Total events in the working set.")]
	int WorkingSetSize,
	[property: Description("Working set events, sorted by commit position (chronological order).")]
	InquiryEvent[] Events);

[McpServerToolType]
public class ViewTool {
	[McpServerTool(Name = "inq_view", UseStructuredContent = true), Description(
		"Show the inquiry's working set, ordered chronologically.")]
	public static Task<ViewResult> View(
		InquiryManager inquiries,
		WorkspaceContext workspaceContext,
		[Description("The inquiry ID.")] string inquiryId) {
		var inquiry = inquiries.Get(inquiryId, workspaceContext.Current) ?? throw new InquiryNotFoundException(inquiryId);

		var events = new InquiryEvent[inquiry.WorkingSetSize];
		var i = 0;
		foreach (var e in inquiry.Events)
			events[i++] = InquiryHelpers.ToInquiryEvent(e);
		Array.Sort(events, (a, b) => a.Id.CompareTo(b.Id));
		return Task.FromResult(new ViewResult(events.Length, events));
	}
}