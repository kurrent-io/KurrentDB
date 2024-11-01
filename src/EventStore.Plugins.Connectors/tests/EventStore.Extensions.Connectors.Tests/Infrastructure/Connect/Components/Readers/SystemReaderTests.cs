// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System.Text.RegularExpressions;
using EventStore.Core;
using EventStore.Core.Data;
using EventStore.Streaming;
using EventStore.Streaming.Consumers;
using EventStore.Streaming.Readers;
using EventStore.Toolkit.Testing.Xunit;
using Shouldly;
using EventStoreCore = EventStore.Core.Services.Transport.Common;

namespace EventStore.Extensions.Connectors.Tests.Connect.Readers;

[Trait("Category", "Integration")]
public class SystemReaderTests(ITestOutputHelper output, ConnectorsAssemblyFixture fixture) : ConnectorsIntegrationTests(output, fixture) {
    [Theory]
    [InlineData(3, 1)]
    public Task reads_from_beginning(int expectedCount, int bufferSize) => Fixture.TestWithTimeout(TimeSpan.FromSeconds(10),
        async ct => {
            // Arrange
            var streamId = Fixture.NewStreamId();

            var records = new List<EventStoreRecord>();

            await using var reader = Fixture.NewReader()
                .ReaderId($"{streamId}-rdr")
                .Create();

            await foreach (var record in reader.Read(LogPosition.Earliest, ReadDirection.Forwards, ConsumeFilter.None, bufferSize, ct.Token)) {
                records.Add(record);

                if (records.Count == expectedCount)
                    await ct.CancelAsync();
            }
        });

    [Theory]
    [InlineData(1, 1000, ConsumeFilterType.StreamId)]
    [InlineData(10, 1000, ConsumeFilterType.Regex)]
    public Task reads_stream_from_beginning(int expectedCount, int bufferSize, ConsumeFilterType filterType) => Fixture.TestWithTimeout(TimeSpan.FromSeconds(10),
        async ct => {
            // Arrange
            var streamId = Fixture.NewStreamId();

            var filter = filterType == ConsumeFilterType.StreamId
                ? ConsumeFilter.FromStreamId(streamId)
                : ConsumeFilter.FromRegex(ConsumeFilterScope.Stream, new Regex(streamId));

            await Fixture.ProduceTestEvents(streamId, expectedCount);

            var records = new List<EventStoreRecord>();

            await using var reader = Fixture.NewReader()
                .ReaderId($"{streamId}-rdr")
                .Create();

            var lastRecordPosition = await Fixture.Reader.GetStreamEarliestPosition(streamId, ct.Token);

            // Act
            await foreach (var record in reader.ReadForwards(lastRecordPosition, filter, bufferSize, ct.Token)) {
                records.Add(record);

                if (records.Count == expectedCount)
                    await ct.CancelAsync();
            }

            // Assert
            records.Count.ShouldBe(expectedCount);
            records.First().StreamId.Value.ShouldStartWith(streamId);
        });

    [Theory, ConsumeFilterCases]
    public Task reads_stream_from_end_backwards(string streamId, ConsumeFilter filter) => Fixture.TestWithTimeout(TimeSpan.FromSeconds(10),
        async ct => {
            // Arrange
            var requests = await Fixture.ProduceTestEvents(streamId);
            var messages = requests.SelectMany(x => x.Messages).ToList();

            var expectedStreamRevision = StreamRevision.From(messages.Count - 1);

            var records = new List<EventStoreRecord>();

            await using var reader = Fixture.NewReader()
                .ReaderId($"{streamId}-rdr")
                .Create();

            // Act
            await foreach (var record in reader.ReadBackwards(LogPosition.Latest, filter, int.MaxValue, ct.Token)) {
                records.Add(record);

                if (records.Count == messages.Count)
                    await ct.CancelAsync();
            }

            // Assert
            records.Count.ShouldBe(messages.Count);
            records.First().Position.StreamRevision.ShouldBe(expectedStreamRevision);
        });

    [Fact]
    public Task reads_single_record() => Fixture.TestWithTimeout(TimeSpan.FromSeconds(10),
        async ct => {
            // Arrange
            var streamId = Fixture.NewStreamId();

            var requests = await Fixture.ProduceTestEvents(streamId, 1, 1);

            var expectedPosition = requests.First().Position;

            await using var reader = Fixture.NewReader()
                .ReaderId($"{streamId}-rdr")
                .Create();

            // Act
            var result = await reader.ReadRecord(expectedPosition, ct.Token);

            // Assert
            result.Position.ShouldBeEquivalentTo(expectedPosition);
        });

    [Fact]
    public Task reads_last_stream_record() => Fixture.TestWithTimeout(TimeSpan.FromSeconds(10),
        async ct => {
            // Arrange
            var streamId = Fixture.NewStreamId();

            var requests = await Fixture.ProduceTestEvents(streamId, 1);
            var messages = requests.SelectMany(x => x.Messages).ToList();

            var expectedStreamRevision = EventStoreCore.StreamRevision.FromInt64(messages.Count - 1);

            await using var reader = Fixture.NewReader()
                .ReaderId($"{streamId}-rdr")
                .Create();

            // Act
            var result = await reader.ReadLastStreamRecord(streamId, ct.Token);

            // Assert
            result.Position.StreamRevision.Value.ShouldBe(expectedStreamRevision.ToInt64());
        });

    [Fact]
    public Task reads_last_stream_record_and_returns_none_when_stream_is_missing() => Fixture.TestWithTimeout(TimeSpan.FromSeconds(10),
        async ct => {
            // Arrange
            var streamId = Fixture.NewStreamId();

            await using var reader = Fixture.NewReader()
                .ReaderId($"{streamId}-rdr")
                .Create();

            // Act
            var result = await reader.ReadLastStreamRecord(streamId, ct.Token);

            // Assert
            result.ShouldBe(EventStoreRecord.None);
        });

    [Fact]
    public Task reads_last_stream_record_and_returns_none_when_stream_exists_but_has_no_records() => Fixture.TestWithTimeout(TimeSpan.FromSeconds(10),
        async ct => {
            // Arrange
            var streamId = Fixture.NewStreamId();

            await Fixture.ProduceTestEvents(streamId, 1);

            await Fixture.Publisher.SetStreamMetadata(streamId,
                new StreamMetadata(truncateBefore: int.MaxValue),
                cancellationToken: ct.Token);

            await using var reader = Fixture.NewReader()
                .ReaderId($"{streamId}-rdr")
                .Create();

            // Act
            var result = await reader.ReadLastStreamRecord(streamId, ct.Token);

            // Assert
            result.ShouldBe(EventStoreRecord.None);
        });

    [Fact]
    public Task reads_last_stream_record_and_returns_none_when_stream_is_soft_deleted() => Fixture.TestWithTimeout(TimeSpan.FromSeconds(10),
        async ct => {
            // Arrange
            var streamId = Fixture.NewStreamId();

            var requests     = await Fixture.ProduceTestEvents(streamId, 1, 1);
            var streamRecord = requests.First();

            await Fixture.Publisher.SoftDeleteStream(streamId,
                streamRecord.Position.StreamRevision.Value,
                cancellationToken: ct.Token);

            await using var reader = Fixture.NewReader()
                .ReaderId($"{streamId}-rdr")
                .Create();

            // Act
            var result = await reader.ReadLastStreamRecord(streamId, ct.Token);

            // Assert
            result.ShouldBe(EventStoreRecord.None);
        });

    class ConsumeFilterCases : TestCaseGenerator<ConsumeFilterCases> {
        protected override IEnumerable<object[]> Data() {
            var streamId = Guid.NewGuid().ToString();
            yield return [streamId, ConsumeFilter.FromStreamId(streamId)];

            streamId = Guid.NewGuid().ToString();
            yield return [streamId, ConsumeFilter.FromRegex(ConsumeFilterScope.Stream, new Regex(streamId))];
        }
    }
}