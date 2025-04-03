// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using NUnit.Framework;

namespace EventStore.Core.Tests.AwakeService;

[TestFixture]
public class when_creating {
	[Test]
	public void it_can_ce_created() {
		var it = new Core.Services.AwakeReaderService.AwakeService();
		Assert.NotNull(it);
	}
}
