// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.LogV3;
using Xunit;
using StreamId = System.UInt32;

namespace EventStore.Core.XUnit.Tests.LogV3;

public class LogV3StreamExistenceFilterInitializerTests {
	[Fact]
	public async Task can_initialize_empty() {
		var sut = new LogV3StreamExistenceFilterInitializer(new MockNameLookup(new()));

		var filter = new MockExistenceFilter();
		await filter.Initialize(sut, 0, CancellationToken.None);

		Assert.Equal(-1, filter.CurrentCheckpoint);
		Assert.Empty(filter.Streams);
	}

	[Fact]
	public async Task can_initialize_non_empty() {
		var sut = new LogV3StreamExistenceFilterInitializer(new MockNameLookup(
			new Dictionary<StreamId, string> {
				{ 1024, "1024" },
				{ 1026, "1026" },
		}));

		var filter = new MockExistenceFilter();
		await filter.Initialize(sut, 0, CancellationToken.None);

		Assert.Equal(1026, filter.CurrentCheckpoint);
		Assert.True(filter.MightContain("1024"));
		Assert.True(filter.MightContain("1026"));
		Assert.False(filter.MightContain("1028"));
	}
}
