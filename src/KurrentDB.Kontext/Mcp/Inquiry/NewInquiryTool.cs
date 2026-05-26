// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.ComponentModel;
using KurrentDB.Kontext.Workspaces;
using KurrentDB.Kontext.Workspaces.Api;
using KurrentDB.Kontext.Workspaces.Registry;
using ModelContextProtocol.Server;

namespace KurrentDB.Kontext.Mcp.Inquiry;

public sealed record NewInquiryResult(
	[property: Description("Identifier of the new inquiry; pass it to the other inq_ tools.")]
	string InquiryId);

[McpServerToolType]
public class NewInquiryTool {
	[McpServerTool(Name = "inq_new", UseStructuredContent = true), Description(
		"Open a new inquiry — a working set that accumulates events across multiple inq_search / inq_read operations. " +
		"Multiple inquiries can run concurrently — open separate inquiries to explore independent questions without cross-contaminating their working sets. " +
		"Always call inq_end when finished; otherwise the inquiry is automatically closed after 1 hour of inactivity.")]
	public static NewInquiryResult NewInquiry(
		InquiryManager inquiries,
		WorkspaceContext workspaceContext,
		WorkspaceRegistry workspaces) {
		var workspace = workspaceContext.Current;
		if (!workspaces.TryGet(workspace, out var entry))
			throw new WorkspaceNotFoundException(workspace);

		entry.EnsureInquiriesEnabled();

		return new NewInquiryResult(inquiries.Create(workspace).Id);
	}
}