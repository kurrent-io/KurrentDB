// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Kontext.Workspaces;

namespace KurrentDB.Kontext.Tests.Mcp.Inquiry;

public class EndInquiryToolTests {
	[Test]
	public async Task EndInquiry_Removes_Existing_Inquiry() {
		var inquiries = new InquiryManager();
		var inquiry = inquiries.Create(WorkspaceNaming.DefaultName);
		EndInquiryTool.EndInquiry(inquiries, new FakeWorkspaceContext(), inquiry.Id);

		await Assert.That(inquiries.Get(inquiry.Id, WorkspaceNaming.DefaultName)).IsNull();
	}

	[Test]
	public async Task EndInquiry_Nonexistent_Throws() {
		var inquiries = new InquiryManager();
		await Assert.That(() => EndInquiryTool.EndInquiry(inquiries, new FakeWorkspaceContext(), "nope"))
			.Throws<InquiryNotFoundException>();
	}
}