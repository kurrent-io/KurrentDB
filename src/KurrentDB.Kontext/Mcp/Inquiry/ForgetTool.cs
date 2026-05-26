// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.ComponentModel;
using KurrentDB.Kontext.Workspaces.Api;
using ModelContextProtocol.Server;

namespace KurrentDB.Kontext.Mcp.Inquiry;

public sealed record ForgetResult(
	[property: Description("Total events forgotten by this call.")]
	int Forgotten,
	[property: Description("Total events in the inquiry's working set after this call.")]
	int WorkingSetSize);

[McpServerToolType]
public class ForgetTool {
	[McpServerTool(Name = "inq_forget", UseStructuredContent = true), Description(
		"Drop events from the inquiry by id. Forgotten events stay excluded from future inq_search / inq_read for the inquiry's lifetime.")]
	public static ForgetResult Forget(
		WorkspaceContext workspaceContext,
		InquiryManager inquiries,
		[Description("The inquiry ID.")] string inquiryId,
		[Description("Identifiers of events to forget (the 'id' field on returned events).")] long[] eventIds) {
		var inquiry = inquiries.Get(inquiryId, workspaceContext.Current) ?? throw new InquiryNotFoundException(inquiryId);

		var forgotten = 0;
		foreach (var id in eventIds) {
			if (inquiry.TryForget(id))
				forgotten++;
		}

		return new ForgetResult(forgotten, inquiry.WorkingSetSize);
	}
}