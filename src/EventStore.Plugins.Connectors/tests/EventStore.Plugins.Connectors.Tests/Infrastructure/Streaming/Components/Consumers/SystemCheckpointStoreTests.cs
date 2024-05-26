// ReSharper disable CheckNamespace

using EventStore.Core;
using EventStore.Streaming;
using EventStore.Streaming.Consumers.Checkpoints;

namespace EventStore.Plugins.Connectors.Tests.Consumers.Checkpoints;

[Trait("Category", "Integration")]
public class SystemCheckpointStoreTests(ITestOutputHelper output, StreamingFixture fixture) : StreamingTests(output, fixture) {
	[Fact]
	public async Task returns_empty_positions_when_stream_not_found() {
		// Arrange
		var groupId    = Identifiers.GenerateShortId("grp");
		var consumerId = Identifiers.GenerateShortId("csr");

		var sut = new SystemCheckpointStore(Fixture.Publisher, groupId, consumerId, 1);

		// Act
		var positions = await sut.GetLatestPositions();
		
		// Assert
		positions.Should().BeEmpty();

		var result = await Fixture.Publisher.GetStreamMetadata(sut.CheckpointStreamId);
		result.Metadata.MaxCount.Should().Be(1);
	}

	[Fact]
	public async Task commits_positions() {
		// Arrange
		var groupId    = Identifiers.GenerateShortId("grp");
		var consumerId = Identifiers.GenerateShortId("csr");

		var expectedPositions = new[] {
			new RecordPosition {
				StreamId       = StreamId.From(Fixture.NewStreamId()),
				StreamRevision = StreamRevision.Max,
				LogPosition    = LogPosition.Latest,
				PartitionId    = PartitionId.From(23)
			}
		};

		var sut = new SystemCheckpointStore(Fixture.Publisher, groupId, consumerId);

		// Act
		var result = await sut.CommitPositions(expectedPositions);
		
		// Assert
		var actualPositions = await sut.GetLatestPositions();
		actualPositions.Should().BeEquivalentTo(expectedPositions); 
	}
	
	[Fact]
	public async Task resets_positions() {
		// Arrange
		var groupId    = Identifiers.GenerateShortId("grp");
		var consumerId = Identifiers.GenerateShortId("csr");

		var expectedPositions = new[] {
			new RecordPosition {
				StreamId       = StreamId.From(Fixture.NewStreamId()),
				StreamRevision = StreamRevision.Max,
				LogPosition    = LogPosition.Latest,
				PartitionId    = PartitionId.From(10)
			}
		};

		var sut = new SystemCheckpointStore(Fixture.Publisher, groupId, consumerId);
		
		await sut.CommitPositions(expectedPositions);
		
		// Act
		await sut.ResetPositions();
		
		// Assert
		var actualPositions = await sut.GetLatestPositions();
		actualPositions.Should().BeEmpty(); 
	}
	
	[Fact]
	public async Task deletes_positions() {
		// Arrange
		var groupId    = Identifiers.GenerateShortId("grp");
		var consumerId = Identifiers.GenerateShortId("csr");
		
		var expectedPositions = new[] {
			new RecordPosition {
				StreamId       = StreamId.From(Fixture.NewStreamId()),
				StreamRevision = StreamRevision.Max,
				LogPosition    = LogPosition.Latest,
				PartitionId    = PartitionId.From(23)
			}
		};

		var sut = new SystemCheckpointStore(Fixture.Publisher, groupId, consumerId);
		
		await sut.CommitPositions(expectedPositions);
		await sut.CommitPositions(expectedPositions);
		await sut.CommitPositions(expectedPositions);
		
		// Act
		await sut.DeletePositions();
		
		// Assert
		var exists = await Publisher.StreamExists(sut.CheckpointStreamId);
		exists.Should().BeFalse();
		
		var actualPositions = await sut.GetLatestPositions();
		actualPositions.Should().BeEmpty(); 
	}
	
	[Fact]
	public async Task commits_positions_after_delete() {
		// Arrange
		var groupId    = Identifiers.GenerateShortId("grp");
		var consumerId = Identifiers.GenerateShortId("csr");

		var expectedPositions = new[] {
			new RecordPosition {
				StreamId       = StreamId.From(Fixture.NewStreamId()),
				StreamRevision = StreamRevision.Max,
				LogPosition    = LogPosition.Latest,
				PartitionId    = PartitionId.From(10)
			}
		};

		var sut = new SystemCheckpointStore(Fixture.Publisher, groupId, consumerId);
		
		await sut.CommitPositions([RecordPosition.Unset]);
		await sut.CommitPositions([RecordPosition.Unset]);
		await sut.CommitPositions([RecordPosition.Unset]);
		
		// Act
		var all = await Publisher.ReadFullStream(sut.CheckpointStreamId).ToListAsync();
		await sut.DeletePositions();
		
		// Assert
		var exists = await Publisher.StreamExists(sut.CheckpointStreamId);
		exists.Should().BeFalse();
		
		var actualPositions = await sut.GetLatestPositions();
		actualPositions.Should().BeEmpty(); 
		
		await sut.CommitPositions(expectedPositions);
		
		var actualPositions2 = await sut.GetLatestPositions();
		actualPositions2.Should().BeEquivalentTo(expectedPositions); 
		
		// var all2 = await Publisher.Execute(new SystemContracts.ReadStream(sut.CheckpointStreamId, Core.Services.Transport.Common.StreamRevision.Start, 1000)).ToListAsync();
	}
	
	// // not sure what to do here to be honest...
	// [Fact]
	// public async Task fails_to_commit_positions_when_stream_not_found() {
	// 	// Arrange
	// 	var groupId    = Identifiers.GenerateShortId("grp");
	// 	var consumerId = Identifiers.GenerateShortId("csr");
	//
	// 	var sut = new SystemCheckpointStore(Fixture.Publisher, groupId, consumerId, 1);
	// 	
	// 	await sut.CommitPositions([RecordPosition.Unset]);
	// 	await sut.DeletePositions();
	// 	
	// 	// Act & Assert
	// 	var exists = await Publisher.StreamExists(sut.CheckpointStreamId);
	// 	exists.Should().BeFalse();
	// 	
	// 	var action = async () => await sut.CommitPositions([RecordPosition.Unset]);
	// 	await action.Should().ThrowAsync<ReadResponseException.StreamNotFound>();
	// }
}