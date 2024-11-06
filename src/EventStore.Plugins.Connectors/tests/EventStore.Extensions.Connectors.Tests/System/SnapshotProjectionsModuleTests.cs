// ReSharper disable InconsistentNaming

using EventStore.Connectors.Infrastructure;
using EventStore.Connectors.Management.Contracts.Queries;
using EventStore.Streaming;
using EventStore.Toolkit.Testing.Fixtures;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Google.Protobuf.WellKnownTypes;
using Shouldly;

namespace EventStore.Connectors.Tests.Management;

[Trait("Category", "System")]
[Trait("Category", "SnapshotProjections")]
public class SnapshotProjectionsModuleTests(ITestOutputHelper output, FastFixture fixture) : FastTests(output, fixture) {
    readonly SnapshotProjectionsStore Store    = new SnapshotProjectionsStore();
    readonly StreamId                 StreamId = fixture.NewStreamId();

    [Fact]
    public void valid_snapshot() {
        // Arrange & Act
        var exception = Record.Exception(() => new ValidSnapshotProjectionsModule(Store, StreamId));

        // Assert
        exception.Should().BeNull();
    }

    [Fact]
    public void valid_event() {
        // Arrange
        var module = new ValidSnapshotProjectionsModule(Store, StreamId);

        // Act
        var exception = Record.Exception(() => module.UpdateWhen<ValidEvent>((snapshot, evt) => null!));

        // Assert
        exception.Should().BeNull();
    }

    [Fact]
    public void missing_snapshot_metadata_should_throw() {
        // Arrange & Act
        var exception = Record.Exception(() => new InvalidSnapshotProjectionsModule(Store, StreamId));

        // Assert
        exception.ShouldNotBeNull();
        exception.Should().BeOfType<InvalidOperationException>();
        exception.Message.Should().Contain($"{nameof(InvalidSnapshot)} must contain a '{nameof(ValidSnapshot.Metadata)}' property", exception.Message);
    }

    [Fact]
    public void missing_event_timestamp_should_throw() {
        // Arrange
        var module = new ValidSnapshotProjectionsModule(Store, StreamId);

        // Act
        var exception = Record.Exception(() => module.UpdateWhen<InvalidEvent>((snapshot, evt) => null!));

        // Assert
        exception.ShouldNotBeNull();
        exception.Should().BeOfType<InvalidOperationException>();
        exception.Message.Should().Contain($"{nameof(InvalidEvent)} must contain a '{nameof(ValidEvent.Timestamp)}' property");
    }

    [Fact]
    public void valid_event_with_custom_timestamp_property_should_not_throw() {
        // Arrange
        var module = new ValidSnapshotProjectionsModule(Store, StreamId);

        // Act
        var exception = Record.Exception(() =>
            module.UpdateWhen<ValidEventCustomTimestamp>((snapshot, evt) => {
                    snapshot.Metadata.UpdateTime = evt.CustomTimestamp;
                    return snapshot;
                },
                getTimestamp: e => e.CustomTimestamp));

        // Assert
        exception.Should().BeNull();
    }
}

public class SnapshotProjectionsStore : ISnapshotProjectionsStore {
    public Task<(TSnapshot Snapshot, RecordPosition Position)> LoadSnapshot<TSnapshot>(StreamId snapshotStreamId) where TSnapshot : class, IMessage, new() =>
        Task.FromResult((new TSnapshot(), new RecordPosition()));

    public Task SaveSnapshot<TSnapshot>(StreamId snapshotStreamId, StreamRevision expectedRevision, TSnapshot snapshot)
        where TSnapshot : class, IMessage, new() =>
        Task.CompletedTask;
}

public class ValidSnapshotProjectionsModule(ISnapshotProjectionsStore store, StreamId snapshotStreamId) : SnapshotProjectionsModule<ValidSnapshot>(store, snapshotStreamId) {
    public new void UpdateWhen<T>(UpdateSnapshot<ValidSnapshot, T> update, Func<T, Timestamp>? getTimestamp = null) where T : class, IMessage, new() =>
        base.UpdateWhen(update, getTimestamp);
}

public class InvalidSnapshotProjectionsModule(ISnapshotProjectionsStore store, StreamId snapshotStreamId) : SnapshotProjectionsModule<InvalidSnapshot>(store, snapshotStreamId);

public class Message : IMessage {
    public void              MergeFrom(CodedInputStream input) { }
    public void              WriteTo(CodedOutputStream output) { }
    public int               CalculateSize()                   => 0;
    public MessageDescriptor Descriptor                        => null!;
}

public class InvalidSnapshot : Message;

public class ValidSnapshot : Message {
    public SnapshotMetadata Metadata { get; set; } = new SnapshotMetadata();
}

public class InvalidEvent : Message;

public class ValidEvent : Message {
    public Timestamp Timestamp { get; set; } = new Timestamp();
}

public class ValidEventCustomTimestamp : Message {
    public Timestamp CustomTimestamp { get; set; } = new Timestamp();
}