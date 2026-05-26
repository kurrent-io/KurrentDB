// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.ComponentModel;
using KurrentDB.Kontext.Workspaces.Api;
using ModelContextProtocol.Server;

namespace KurrentDB.Kontext.Mcp.Inquiry;

[McpServerToolType]
public class EndInquiryTool {
	[McpServerTool(Name = "inq_end"), Description(
		"End an inquiry and free its resources.")]
	public static void EndInquiry(
		InquiryManager inquiries,
		WorkspaceContext workspaceContext,
		[Description("The inquiry ID to end.")] string inquiryId) {
		if (!inquiries.Delete(inquiryId, workspaceContext.Current))
			throw new InquiryNotFoundException(inquiryId);
	}
}