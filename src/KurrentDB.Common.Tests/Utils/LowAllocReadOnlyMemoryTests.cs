// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Common.Utils;

namespace KurrentDB.Common.Tests.Utils;

public class LowAllocReadOnlyMemoryTests {
	[Fact]
	public void empty_works() {
		var sut = LowAllocReadOnlyMemory<int>.Empty;
		Assert.Equal(0, sut.Length);
		Assert.Throws<InvalidOperationException>(() => sut.Single);
		Assert.Equal(Array.Empty<int>(), sut.Span);

		foreach (var x in sut) {
			Assert.Fail();
		}
	}

	[Fact]
	public void single_works() {
		var expected = new int[] { 5 };
		var sut = new LowAllocReadOnlyMemory<int>(5);
		Assert.Equal(1, sut.Length);
		Assert.Equal(5, sut.Single);
		Assert.Equal(expected, sut.Span);

		var foreachOutput = new List<int>();
		foreach (var x in sut) {
			foreachOutput.Add(x);
		}
		Assert.Equal(expected, foreachOutput);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(1)]
	[InlineData(2)]
	[InlineData(3)]
	public void multiple_works(int length) {
		var expected = Enumerable.Range(0, length).ToArray();
		var sut = new LowAllocReadOnlyMemory<int>(expected);
		Assert.Equal(expected.Length, sut.Length);

		if (length is 1) {
			Assert.Equal(0, sut.Single);
		} else {
			Assert.Throws<InvalidOperationException>(() => sut.Single);
		}

		Assert.Equal(expected, sut.Span);

		var foreachOutput = new List<int>();
		foreach (var x in sut) {
			foreachOutput.Add(x);
		}
		Assert.Equal(expected, foreachOutput);
	}

	[Fact]
	public void collection_expression_works() {
		LowAllocReadOnlyMemory<int> sut = [];
		Assert.Equal(0, sut.Length);

		sut = [2];
		Assert.Equal(2, sut.Single);

		sut = [3, 4, 5];
		var expected = new int[] { 3, 4, 5 };
		Assert.Equal(expected, sut.Span);
	}
}
