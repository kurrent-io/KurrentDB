// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Kontext.Workspaces;

namespace KurrentDB.Kontext.Tests.Mcp.Inquiry;

public class NewInquiryToolTests {
	[Test]
	public async Task NewInquiry_Returns_Id() {
		var inquiries = new InquiryManager();
		var result = NewInquiryTool.NewInquiry(inquiries, new FakeWorkspaceContext(), TestWorkspace.Registry());

		await Assert.That(result.InquiryId).IsNotNullOrEmpty();
	}

	[Test]
	public async Task NewInquiry_Throws_When_Inquiries_Disabled() {
		var inquiries = new InquiryManager();
		var registry = TestWorkspace.Registry(disableInquiries: true);

		await Assert.That(() => NewInquiryTool.NewInquiry(inquiries, new FakeWorkspaceContext(), registry))
			.Throws<WorkspaceOperationDisabledException>();
	}
}