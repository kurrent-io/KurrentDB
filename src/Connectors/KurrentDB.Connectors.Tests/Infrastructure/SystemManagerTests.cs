// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Surge;
using KurrentDB.Core;
using Exception = System.Exception;
using StreamMetadata = KurrentDB.Core.Data.StreamMetadata;

namespace KurrentDB.Connectors.Tests.Infrastructure;

[Trait("Category", "Integration")]
public class SystemManagerTests(ITestOutputHelper output, ConnectorsAssemblyFixture fixture) : ConnectorsIntegrationTests(output, fixture) {
    [Fact]
    public async Task returns_false_when_stream_does_not_exist() => await Fixture.TestWithTimeout(async ct => {
        // Arrange
        var streamId = Fixture.NewStreamId();

        // Act
        var exists = await Fixture.Manager.StreamExists(streamId, ct.Token);

        // Assert
        exists.Should().BeFalse();
    });

    [Fact]
    public async Task returns_true_when_stream_exists() => await Fixture.TestWithTimeout(async ct => {
        // Arrange
        var streamId = Fixture.NewStreamId();
        await Fixture.ProduceTestEvents(streamId);

        // Act
        var exists = await Fixture.Manager.StreamExists(streamId, ct.Token);

        // Assert
        exists.Should().BeTrue();
    });

    [Fact]
    public async Task stream_exists_after_deletion_returns_false() => await Fixture.TestWithTimeout(async ct => {
        // Arrange
        var streamId = Fixture.NewStreamId();
        await Fixture.Manager.DeleteStream(streamId, ct.Token);

        // Act
        var exists = await Fixture.Manager.StreamExists(streamId, ct.Token);

        // Assert
        exists.Should().BeFalse();
    });

    [Fact]
    public async Task delete_stream_non_existent_stream_returns_stream_not_found_error() => await Fixture.TestWithTimeout(async ct => {
        // Arrange
        var streamId = Fixture.NewStreamId();

        // Act
        var result = await Fixture.Manager.DeleteStream(streamId, ct.Token);

        // Assert
        result.Value.Should().BeOfType<StreamNotFoundError>();
    });

    [Fact]
    public async Task delete_stream_with_correct_expected_revision_deletes_successfully() => await Fixture.TestWithTimeout(async ct => {
        // Arrange
        var streamId = Fixture.NewStreamId();
        var events   = await Fixture.ProduceTestEvents(streamId, 1, 1);
        events[^1].Position.StreamRevision.Should().Be(StreamRevision.From(0));

        // Act
        var result = await Fixture.Manager.DeleteStream(streamId, expectedStreamRevision: StreamRevision.From(0), ct.Token);

        // Assert
        result.Value.Should().BeOfType<LogPosition>();
    });

    [Fact]
    public async Task delete_stream_with_incorrect_expected_revision_returns_expected_stream_revision_error() => await Fixture.TestWithTimeout(async ct => {
        // Arrange
        var streamId = Fixture.NewStreamId();
        await Fixture.ProduceTestEvents(streamId, 1, 1);

        // Act
        var result = await Fixture.Manager.DeleteStream(streamId, StreamRevision.From(1), ct.Token);

        // Assert
        result.Value.Should().BeOfType<ExpectedStreamRevisionError>();
    });

    [Fact]
    public async Task delete_stream_with_valid_log_position_deletes_successfully() => await Fixture.TestWithTimeout(async ct => {
        // Arrange
        var streamId = Fixture.NewStreamId();
        await Fixture.ProduceTestEvents(streamId, 1, 1);
        var infoResult  = await Fixture.Manager.GetStreamInfo(LogPosition.Earliest, ct.Token);
        var logPosition = infoResult.Match(pos => pos, _ => throw new Exception("Failed to get log position"));

        // Act
        var result = await Fixture.Manager.DeleteStream(streamId, LogPosition.From(logPosition.Revision), ct.Token);

        // Assert
        result.Value.Should().BeOfType<LogPosition>();
    });

    [Fact]
    public async Task configure_stream_updates_metadata_when_changes_are_made() => await Fixture.TestWithTimeout(async ct => {
        // Arrange
        var streamId = Fixture.NewStreamId();
        await Fixture.ProduceTestEvents(streamId);
        await Fixture.Publisher.SetStreamMetadata(streamId, StreamMetadata.Empty, cancellationToken: ct.Token);
        const int newMaxCount = 100;

        // Act
        var newMetadata = await Fixture.Manager.ConfigureStream(streamId,
            metadata => metadata with {
                MaxCount = newMaxCount
            },
            ct.Token);

        // Assert
        newMetadata.MaxCount.Should().Be(newMaxCount);
        var retrievedMetadata = await Fixture.Manager.GetStreamMetadata(streamId, ct.Token);
        retrievedMetadata.MaxCount.Should().Be(newMaxCount);
    });

    [Fact]
    public async Task configure_stream_does_not_update_metadata_when_no_changes() => await Fixture.TestWithTimeout(async ct => {
        // Arrange
        var streamId = Fixture.NewStreamId();
        await Fixture.ProduceTestEvents(streamId);
        await Fixture.Publisher.SetStreamMetadata(streamId, StreamMetadata.Empty, cancellationToken: ct.Token);
        var initialMetadata = await Fixture.Manager.GetStreamMetadata(streamId, ct.Token);

        // Act
        var newMetadata = await Fixture.Manager.ConfigureStream(streamId, metadata => metadata, ct.Token);

        // Assert
        newMetadata.Should().BeEquivalentTo(initialMetadata);
    });

    [Fact]
    public async Task get_stream_metadata_returns_metadata_when_set() => await Fixture.TestWithTimeout(async ct => {
        // Arrange
        var streamId = Fixture.NewStreamId();
        await Fixture.ProduceTestEvents(streamId);
        await Fixture.Publisher.SetStreamMetadata(streamId, StreamMetadata.Empty, cancellationToken: ct.Token);
        const int maxCount = 100;

        await Fixture.Manager.ConfigureStream(streamId,
            metadata => metadata with {
                MaxCount = maxCount
            },
            ct.Token);

        // Act
        var metadata = await Fixture.Manager.GetStreamMetadata(streamId, ct.Token);

        // Assert
        metadata.MaxCount.Should().Be(maxCount);
    });

    [Fact]
    public async Task get_stream_metadata_returns_empty_for_non_existent_stream() => await Fixture.TestWithTimeout(async ct => {
        // Arrange
        var streamId = Fixture.NewStreamId();

        // Act
        var metadata = await Fixture.Manager.GetStreamMetadata(streamId, ct.Token);

        // Assert
        metadata.Acl.Should().BeNull();
        metadata.CacheControl.Should().BeNull();
        metadata.MaxAge.Should().BeNull();
        metadata.MaxCount.Should().BeNull();
        metadata.TruncateBefore.Should().BeNull();
    });

    [Fact]
    public async Task configure_stream_sets_acl_roles_correctly() => await Fixture.TestWithTimeout(async ct => {
        // Arrange
        var streamId = Fixture.NewStreamId();
        await Fixture.ProduceTestEvents(streamId);
        await Fixture.Publisher.SetStreamMetadata(streamId, StreamMetadata.Empty, cancellationToken: ct.Token);

        var acl = new Kurrent.Surge.StreamMetadata.StreamAcl {
            ReadRoles  = ["admin"],
            WriteRoles = ["admin"]
        };

        // Act
        var newMetadata = await Fixture.Manager.ConfigureStream(streamId,
            metadata => metadata with {
                Acl = acl
            },
            ct.Token);

        // Assert
        newMetadata.Acl.Should().BeEquivalentTo(acl);
        var retrievedMetadata = await Fixture.Manager.GetStreamMetadata(streamId, ct.Token);
        retrievedMetadata.Acl.Should().BeEquivalentTo(acl);
    });

    [Fact]
    public async Task get_stream_info_valid_position_returns_correct_stream_and_revision() => await Fixture.TestWithTimeout(async ct => {
        // Arrange
        var streamId = Fixture.NewStreamId();
        var result   = await Fixture.ProduceTestEvents(streamId, 1, 1);

        // Act
        var info = await Fixture.Manager.GetStreamInfo(result[^1].Position.LogPosition, ct.Token);

        // Assert
        var (stream, revision) = info.Match(success => success,
            _ => throw new Exception("Expected success result"));

        stream.Should().Be(streamId);
        revision.Should().Be(StreamRevision.From(0));
    });
}
