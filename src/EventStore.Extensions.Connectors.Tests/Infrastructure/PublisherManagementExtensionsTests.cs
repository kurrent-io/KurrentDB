// ReSharper disable ExplicitCallerInfoArgument

using EventStore.Core;
using EventStore.Core.Data;
using EventStore.Core.Services.Transport.Common;

namespace EventStore.Extensions.Connectors.Tests;

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