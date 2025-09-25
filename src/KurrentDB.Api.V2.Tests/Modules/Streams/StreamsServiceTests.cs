using Google.Protobuf;
using KurrentDB.Api.Tests.Fixtures;
using KurrentDB.Core;
using KurrentDB.Protocol.V2.Streams;
using KurrentDB.Testing.Sample.HomeAutomation;
using Microsoft.Extensions.Logging;

namespace KurrentDB.Api.Tests.Streams;

public class StreamsServiceTests {
    [ClassDataSource<ClusterVNodeTestContext>(Shared = SharedType.PerAssembly)]
    public required ClusterVNodeTestContext Fixture { get; init; }

    [Test]
    public async ValueTask appends_one_record(CancellationToken cancellationToken) {
        // Arrange
        var game = Fixture.SimulateGame(take: 1);

        // Act
        Fixture.Logger.LogInformation(
            "Starting append session for {Stream} stream with a total of {Records} records",
            game.Stream, game.Count());

        using var session = Fixture.StreamsClient.AppendSession(cancellationToken: cancellationToken);

        await session.RequestStream.WriteAsync(game, cancellationToken);
        await session.RequestStream.CompleteAsync();

        Fixture.Logger.LogInformation("Append request sent, awaiting response...");

        var response = await session.ResponseAsync;

        Fixture.Logger.LogInformation("Append session completed at position {Position}", response.Position);

        // Assert
        await Assert.That(response.Output).HasCount(1);
        await Assert.That(response.Position).IsGreaterThanOrEqualTo(1);
    }

    [Test]
    public async ValueTask appends_one_record2(CancellationToken cancellationToken) {
        // Arrange

        var iotEvents = HomeAutomationSimulator.ForHome("").Simulate(100);

        var game = Fixture.SimulateGame(take: 1);

        var currentPosition = await Fixture.SystemClient.GetLastPosition(cancellationToken);
        Fixture.Logger.LogInformation("Current log position {Position}", currentPosition);

        // Act
        Fixture.Logger.LogInformation(
            "Appending {Records} records to {Stream} stream",
            game.Count(),  game.Stream);

         var response = await Fixture.StreamsClient
             .AppendAsync(game, cancellationToken: cancellationToken);

        Fixture.Logger.LogInformation(
            "{TotalRecords} records appended to {Stream} stream v{Revision} at position {Position}",
            game.Count(), game.Stream, response.StreamRevision, response.Position);

        // Assert
        await Assert.That(response.Stream).IsEqualTo(game.Stream);
        await Assert.That(response.StreamRevision).IsEqualTo(game.Count());
        await Assert.That(response.Position).IsGreaterThanOrEqualTo(currentPosition + game.Count());
    }

    [Test]
    [Arguments(5)]
    [Arguments(10)]
    [Arguments(20)]
    public async ValueTask appends_records_to_many_streams(int numberOfStreams, CancellationToken cancellationToken) {
        // Arrange
        var games = Enumerable.Range(1, numberOfStreams).Select(_ => Fixture.SimulateGame()).ToList();

        // Act
        Fixture.Logger.LogInformation(
            "Starting append session for {Streams} streams with a total of {Records} records",
            numberOfStreams, games.Sum(g => g.Count()));

        using var session = Fixture.StreamsClient.AppendSession(cancellationToken: cancellationToken);

        foreach (var game in games) {
            Fixture.Logger.LogInformation("Appending {Count} records to stream {Stream}", game.Count(), game.Stream);
            await session.RequestStream.WriteAsync(game, cancellationToken);
        }

        await session.RequestStream.CompleteAsync();

        Fixture.Logger.LogInformation("All {Streams} requests sent, awaiting response...", numberOfStreams);

        var response = await session.ResponseAsync;

        Fixture.Logger.LogInformation("Append session completed at position {Position}", response.Position);

        // Assert
        await Assert.That(response.Output).HasCount(games.Count);
        await Assert.That(response.Position).IsGreaterThanOrEqualTo(numberOfStreams);
    }


    [Test]
    [Arguments(ExpectedRevisionConstants.Exists)]
    public async ValueTask appends_records_with_expected_revision(long expectedRevision, CancellationToken cancellationToken) {
        // Arrange
        var game = Fixture.SimulateGame(take: 1);

        // Act
        Fixture.Logger.LogInformation(
            "Starting append session for {Stream} stream with a total of {Records} records",
            game.Stream, game.Count());

        using var session = Fixture.StreamsClient.AppendSession(cancellationToken: cancellationToken);

        await session.RequestStream.WriteAsync(game, cancellationToken);
        await session.RequestStream.CompleteAsync();

        Fixture.Logger.LogInformation("Append request sent, awaiting response...");

        var response = await session.ResponseAsync;

        Fixture.Logger.LogInformation("Append session completed at position {Position}", response.Position);

        // Assert
        await Assert.That(response.Output).HasCount(1);
        await Assert.That(response.Position).IsGreaterThanOrEqualTo(1);
    }

    [Test]
    [Skip("Skipped with SkipAttribute")]
    public Task throws_when_user_does_not_have_permissions() => throw new NotImplementedException();

    [Test]
    [Skip("Skipped with SkipAttribute")]
    public async Task throws_when_stream_already_tracked() {
        // Arrange
        var names = new[] { "James", "Jo", "Lee" };

        // Act
        using var session = Fixture.StreamsClient.AppendSession();

        foreach (var name in names) {
            var req = new AppendRequest {
                Stream           = "name",
                ExpectedRevision = 1,
                Records = {
                    new AppendRecord {
                        Schema = new SchemaInfo {
                            Format = SchemaFormat.Bytes,
                            Name   = "name"
                        },
                        Data = ByteString.CopyFromUtf8(name),
                    }
                }
            };

            await session.RequestStream.WriteAsync(req);
        }

        await session.RequestStream.CompleteAsync();

        var response = await session.ResponseAsync;

        // Assert
        await Assert.That(response.Output).IsNotEmpty();
    }

    [Test]
    [Skip("Skipped with SkipAttribute")]
    public Task throws_when_record_is_too_large() {
        //   4,194,304 (4 MB).
        throw new NotImplementedException();
    }

    [Test]
    [Skip("Skipped with SkipAttribute")]
    public Task throws_when_transaction_is_too_large() => throw new NotImplementedException();

    [Test]
    [Skip("Skipped with SkipAttribute")]
    public Task throws_on_stream_revision_conflict() => throw new NotImplementedException();

    [Test]
    [Skip("Skipped with SkipAttribute")]
    public Task throws_on_stream_tombstoned() => throw new NotImplementedException();

    [Test]
    [Skip("Skipped with SkipAttribute")]
    public Task throws_when_request_has_no_records() => throw new NotImplementedException();

    // [Test]
    // [Skip("Skipped with SkipAttribute")]
    // public Task throws_when_stream_name_is_invalid() => throw new NotImplementedException();
    //
    // [Test]
    // [Skip("Skipped with SkipAttribute")]
    // public Task throws_when_record_id_is_invalid() => throw new NotImplementedException();
    //
    // [Test]
    // [Skip("Skipped with SkipAttribute")]
    // public Task throws_when_schema_name_is_invalid() => throw new NotImplementedException();
    //
    // [Test]
    // [Skip("Skipped with SkipAttribute")]
    // public Task throws_when_schema_format_is_invalid() => throw new NotImplementedException();
    //
    // [Test]
    // [Skip("Skipped with SkipAttribute")]
    // public Task throws_when_schema_id_is_invalid() => throw new NotImplementedException();
}
