// ReSharper disable ExplicitCallerInfoArgument

using EventStore.Core;
using EventStore.Core.Data;

namespace EventStore.Extensions.Connectors.Tests;

[Trait("Category", "Integration")]
public class PublisherManagementExtensionsTests(ITestOutputHelper output, ConnectorsAssemblyFixture fixture) : ConnectorsIntegrationTests(output, fixture) {
	[Fact]
	public async Task can_get_stream_metadata_when_stream_not_found() {
		// Arrange
		var streamName = Fixture.NewStreamId("stream");
		var expectedResult = (StreamMetadata.Empty, Core.Services.Transport.Common.StreamRevision.Start);

		// Act
		var result = await Publisher.GetStreamMetadata(streamName);

		// Assert
		result.Should().BeEquivalentTo(expectedResult);
	}

	[Fact]
	public async Task can_get_stream_metadata_when_stream_found() {
		// Arrange
		var streamName = Fixture.NewStreamId("stream");
		var metadata   = new StreamMetadata(maxCount: 10);

		var expectedResult = (metadata, Core.Services.Transport.Common.StreamRevision.Start);

		await Publisher.SetStreamMetadata(streamName, metadata);

		// Act
		var result = await Publisher.GetStreamMetadata(streamName);

		// Assert
		result.Should().BeEquivalentTo(expectedResult);
	}

	[Fact]
	public async Task can_set_stream_metadata() {
		// Arrange
		var streamName = Fixture.NewStreamId("stream");
		var metadata   = new StreamMetadata(maxCount: 10);

		var expectedResult = (metadata, Core.Services.Transport.Common.StreamRevision.Start);

		// Act
		await Publisher.SetStreamMetadata(streamName, metadata);

		// Assert
		var result = await Publisher.GetStreamMetadata(streamName);

		result.Should().BeEquivalentTo(expectedResult);
	}
}