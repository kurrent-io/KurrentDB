// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Services;
using KurrentDB.SecondaryIndexing.Indexes.Default;

namespace KurrentDB.SecondaryIndexing.Tests.Indexes.DefaultIndexReaderTests;

public class ReaderResolveTests : IndexTestBase {
	[Fact]
	public void CanReadStream_WhenStreamIsDefaultIndexName_ReturnsTrue() {
		// When
		var result = Sut.CanReadIndex(SystemStreams.DefaultSecondaryIndex);

		// Then
		Assert.True(result);
	}

	[Fact]
	public void CanReadStream_WhenStreamIsNotDefaultIndexName_ReturnsFalse() {
		// When
		var result = Sut.CanReadIndex("other-stream");

		// Then
		Assert.False(result);
	}
}
