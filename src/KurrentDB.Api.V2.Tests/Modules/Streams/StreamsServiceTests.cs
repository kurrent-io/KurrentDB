using Grpc.Core;
using KurrentDB.Api.Tests.Fixtures;
using KurrentDB.Protocol.V2.Streams;
using KurrentDB.Protocol.V2.Streams.Errors;
using Microsoft.Extensions.Logging;

namespace KurrentDB.Api.Tests.Streams;

public class StreamsServiceTests {
    [ClassDataSource<ClusterVNodeTestContext>(Shared = SharedType.PerAssembly)]
    public required ClusterVNodeTestContext Fixture { get; init; }

    [Test]
    [Arguments(1, 1)]
    [Arguments(1, 10)]
    [Arguments(10, 1)]
    [Arguments(10, 10)]
    [Arguments(50, 1)]
    [Arguments(50, 10)]
    public async ValueTask appends_records(int numberOfStreams, int numberOfEvents, CancellationToken cancellationToken) {
        // Arrange
        var requests = HomeAutomationTestData
            .SimulateHousingComplexActivity(numberOfStreams, numberOfEvents);

        // Act
        Fixture.Logger.LogInformation(
            "Starting append session for {Streams} streams with a total of {Records} records",
            numberOfStreams, numberOfStreams * numberOfEvents);

        using var session = Fixture.StreamsClient.AppendSession(cancellationToken: cancellationToken);

        foreach (var request in requests) {
            Fixture.Logger.LogInformation("Appending {Count} records to stream {Stream}", request.Records.Count, request.Stream);
            await session.RequestStream.WriteAsync(request, cancellationToken);
        }

        await session.RequestStream.CompleteAsync();

        Fixture.Logger.LogInformation("All {Count} requests sent, awaiting response...", numberOfStreams);

        var response = await session.ResponseAsync;

        Fixture.Logger.LogInformation("Append session completed at position {Position}", response.Position);

        // Assert
        await Assert.That(response.Output).HasCount(numberOfStreams);
        await Assert.That(response.Position).IsGreaterThanOrEqualTo(numberOfStreams);
    }

    [Test]
    public async ValueTask appends_records_with_expected_revision(CancellationToken cancellationToken) {
        // Arrange
        var seededActivity = await Fixture.SeedSmartHomeActivity(cancellationToken);

        var request = seededActivity.SimulateMoreEvents();

        var nextExpectedRevision = seededActivity.StreamRevision + request.Records.Count;

        // Act
        var response = await Fixture.StreamsClient.AppendAsync(request, cancellationToken: cancellationToken);

        // Assert
        await Assert.That(response.StreamRevision).IsEqualTo(nextExpectedRevision);
    }

    [Test]
    public async ValueTask throws_on_stream_revision_conflict(CancellationToken cancellationToken) {
        // Arrange
        var seededActivity = await Fixture.SeedSmartHomeActivity(cancellationToken);

        var request = seededActivity.SimulateMoreEvents()
            .WithExpectedRevision(ExpectedRevisionConstants.NoStream.GetHashCode());

        // Act
        var appendTask = async () => await Fixture.StreamsClient.AppendAsync(request, cancellationToken: cancellationToken);

        // Assert
        var rex     = await appendTask.ShouldThrowAsync<RpcException>();
        var details = rex.GetRpcStatus()?.GetDetail<StreamRevisionConflictErrorDetails>();
        await Assert.That(details).IsNotNull();
    }


    [Test]
    public async ValueTask throws_when_request_has_no_records(CancellationToken cancellationToken) {
        // Arrange
        var request  = HomeAutomationTestData.SimulateHomeActivity()
            .With(req => req.Records.Clear());

        var appendTask = async () => await Fixture.StreamsClient.AppendAsync(request, cancellationToken: cancellationToken);

        // Act & Assert
        var rex     = await appendTask.ShouldThrowAsync<RpcException>();
        var details = rex.GetRpcStatus()?.GetDetail<KurrentDB.Protocol.V2.Common.Errors.InvalidRequestErrorDetails>();
        await Assert.That(details).IsNotNull();
    }

    [Test]
    [Skip("Skipped with SkipAttribute")]
    public ValueTask throws_when_user_does_not_have_permissions() => throw new NotImplementedException();

    [Test]
    public async ValueTask throws_when_stream_already_in_session(CancellationToken cancellationToken) {
        // Arrange
        var request = HomeAutomationTestData.SimulateHomeActivity();

        // Act
        using var session = Fixture.StreamsClient.AppendSession(cancellationToken: cancellationToken);

        await session.RequestStream.WriteAsync(request, cancellationToken);
        await session.RequestStream.WriteAsync(request.SimulateMoreEvents(), cancellationToken);

        await session.RequestStream.CompleteAsync();

        // ReSharper disable once AccessToDisposedClosure
        var appendTask = async () => await session.ResponseAsync;

        // Assert
        var rex     = await appendTask.ShouldThrowAsync<RpcException>();
        var details = rex.GetRpcStatus()?.GetDetail<StreamAlreadyInAppendSessionErrorDetails>();

        await Assert.That(details).IsNotNull();
    }

    [Test]
    [Skip("Skipped with SkipAttribute")]
    public Task throws_when_record_is_too_large() {
        //   4,194,304 (4 MB).
        throw new NotImplementedException();
    }

    [Test]
    [Skip("Skipped with SkipAttribute")]
    public Task throws_when_transaction_is_too_large() =>
        throw new NotImplementedException();

    [Test]
    [Skip("Skipped with SkipAttribute")]
    public Task throws_when_a_stream_is_tombstoned() =>
        throw new NotImplementedException();

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
