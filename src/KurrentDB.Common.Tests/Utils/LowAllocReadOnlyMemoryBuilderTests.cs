// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Common.Utils;

namespace KurrentDB.Common.Tests.Utils;

public class LowAllocReadOnlyMemoryBuilderTests {
	[Fact]
	public void can_add_count_and_build() {
		var sut = LowAllocReadOnlyMemory<int>.Builder.Empty;

		Assert.Equal(0, sut.Count);
		Assert.Equal(0, sut.Build().Length);

		sut = sut.Add(5);

		Assert.Equal(1, sut.Count);
		Assert.Equal(1, sut.Build().Length);
		Assert.Equal(5, sut.Build().Single);
		Assert.Equal(5, sut.Build().Span[0]);

		sut = sut.Add(6);

		Assert.Equal(2, sut.Count);
		Assert.Equal(2, sut.Build().Length);
		Assert.Equal(5, sut.Build().Span[0]);
		Assert.Equal(6, sut.Build().Span[1]);
	}

	[Fact]
	public void can_construct_single() {
		var sut = new LowAllocReadOnlyMemory<int>.Builder(5);

		Assert.Equal(1, sut.Count);
		Assert.Equal(5, sut.Build().Single);
		Assert.Equal(5, sut.Build().Span[0]);
	}

	[Fact]
	public void can_construct_multiple() {
		var sut = new LowAllocReadOnlyMemory<int>.Builder([5, 6]);

		Assert.Equal(2, sut.Count);
		Assert.Equal(2, sut.Build().Length);
		Assert.Equal(5, sut.Build().Span[0]);
		Assert.Equal(6, sut.Build().Span[1]);
	}
}
