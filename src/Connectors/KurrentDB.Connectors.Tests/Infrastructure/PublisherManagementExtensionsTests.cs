// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable ExplicitCallerInfoArgument

using KurrentDB.Core;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Transport.Common;

namespace KurrentDB.Connectors.Tests.Infrastructure;

[Trait("Category", "Integration")]
public class PublisherManagementExtensionsTests(ITestOutputHelper output, ConnectorsAssemblyFixture fixture) : ConnectorsIntegrationTests(output, fixture) {
	[Fact]
	public async Task can_get_stream_metadata_when_stream_not_found() {
		// Arrange
		var streamName = Fixture.NewStreamId("stream");
		var expectedResult = (StreamMetadata.Empty, -2);

		// Act
		var result = await Fixture.Publisher.GetStreamMetadata(streamName);

		// Assert
		result.Should().BeEquivalentTo(expectedResult);
	}

	[Fact]
	public async Task can_get_stream_metadata_when_stream_found() {
		// Arrange
		var streamName = Fixture.NewStreamId("stream");
		var metadata   = new StreamMetadata(maxCount: 10);

		var expectedResult = (metadata, StreamRevision.Start.ToInt64());

		await Fixture.Publisher.SetStreamMetadata(streamName, metadata);

		// Act
		var result = await Fixture.Publisher.GetStreamMetadata(streamName);

		// Assert
		result.Should().BeEquivalentTo(expectedResult);
	}

	[Fact]
	public async Task can_set_stream_metadata() {
		// Arrange
		var streamName = Fixture.NewStreamId("stream");
		var metadata   = new StreamMetadata(maxCount: 10);

		var expectedResult = (metadata, StreamRevision.Start.ToInt64());

		// Act
		await Fixture.Publisher.SetStreamMetadata(streamName, metadata);

		// Assert
		var result = await Fixture.Publisher.GetStreamMetadata(streamName);

		result.Should().BeEquivalentTo(expectedResult);
	}
}
