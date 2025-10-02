// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics.Metrics;
using Google.Protobuf;
using Google.Rpc;
using Grpc.Core;
using KurrentDB.Api.Tests.Fixtures;
using KurrentDB.Core.TransactionLog.Chunks;
using KurrentDB.Protocol.V2.Streams;
using KurrentDB.Protocol.V2.Streams.Errors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
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
    public async ValueTask append_session_appends_records(int numberOfStreams, int numberOfEvents, CancellationToken cancellationToken) {
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
    public async ValueTask append_session_appends_records_with_expected_revision(CancellationToken cancellationToken) {
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
    public async ValueTask append_session_throws_on_stream_revision_conflict(CancellationToken cancellationToken) {
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
        await Assert.That(rex.StatusCode).IsEqualTo(StatusCode.FailedPrecondition);
    }

    [Test]
    public async ValueTask append_session_throws_when_request_has_no_records(CancellationToken cancellationToken) {
        // Arrange
        var request = HomeAutomationTestData.SimulateHomeActivity()
            .With(req => req.Records.Clear());

        var appendTask = async () => await Fixture.StreamsClient.AppendAsync(request, cancellationToken: cancellationToken);

        // Act & Assert
        var rex     = await appendTask.ShouldThrowAsync<RpcException>();
        var details = rex.GetRpcStatus()?.GetDetail<BadRequest>();

        await Assert.That(details).IsNotNull();
        await Assert.That(rex.StatusCode).IsEqualTo(StatusCode.InvalidArgument);
    }

    [Test]
    public async ValueTask append_session_throws_when_stream_already_in_session(CancellationToken cancellationToken) {
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
        await Assert.That(rex.StatusCode).IsEqualTo(StatusCode.Aborted);
    }

    [Test]
    public async ValueTask append_session_throws_when_a_stream_is_tombstoned(CancellationToken cancellationToken) {
        // Arrange
        var seededActivity = await Fixture.SeedSmartHomeActivity(cancellationToken);

        await Fixture.SystemClient.Management.HardDeleteStream(seededActivity.Stream, cancellationToken: cancellationToken);

        var request = seededActivity.SimulateMoreEvents();

        // Act
        var appendTask = async () => await Fixture.StreamsClient.AppendAsync(request, cancellationToken: cancellationToken);

        // Assert
        var rex     = await appendTask.ShouldThrowAsync<RpcException>();
        var details = rex.GetRpcStatus()?.GetDetail<StreamTombstonedErrorDetails>();

        await Assert.That(details).IsNotNull();
        await Assert.That(rex.StatusCode).IsEqualTo(StatusCode.FailedPrecondition);
    }

    [Test]
    public async ValueTask append_session_throws_when_user_does_not_have_permissions(CancellationToken cancellationToken) {

        Assert.Fail("Not implemented");

        // Arrange
        var seededActivity = await Fixture.SeedSmartHomeActivity(cancellationToken);

        await Fixture.SystemClient.Management.HardDeleteStream(seededActivity.Stream, cancellationToken: cancellationToken);

        var request = seededActivity.SimulateMoreEvents();

        // Act
        var appendTask = async () => await Fixture.StreamsClient.AppendAsync(request, cancellationToken: cancellationToken);

        // Assert
        var rex     = await appendTask.ShouldThrowAsync<RpcException>();
        var details = rex.GetRpcStatus()?.GetDetail<StreamTombstonedErrorDetails>();

        await Assert.That(details).IsNotNull();
        await Assert.That(rex.StatusCode).IsEqualTo(StatusCode.FailedPrecondition);
    }

    [Test]
    public async ValueTask append_session_throws_when_record_is_too_large(CancellationToken cancellationToken) {
        // Arrange
        var request = HomeAutomationTestData.SimulateHomeActivity(1);

        request.Records[0].Data = UnsafeByteOperations.UnsafeWrap(new byte[TFConsts.EffectiveMaxLogRecordSize]);

        var appendTask = async () => await Fixture.StreamsClient.AppendAsync(request, cancellationToken: cancellationToken);

        // Act & Assert
        var rex     = await appendTask.ShouldThrowAsync<RpcException>();
        var details = rex.GetRpcStatus()?.GetDetail<AppendRecordSizeExceededErrorDetails>();

        await Assert.That(details).IsNotNull();
        await Assert.That(rex.StatusCode).IsEqualTo(StatusCode.InvalidArgument);
    }

    [Test]
    public ValueTask append_session_throws_when_transaction_is_too_large(CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    [Test]
    public async ValueTask append_session_metrics_recorded(CancellationToken cancellationToken) {
        var meterFactory = Fixture.Services.GetRequiredService<IMeterFactory>();
        var collector = new MetricCollector<double>(meterFactory, "Microsoft.AspNetCore.Hosting", "http.server.request.duration");

        // Act
        await Fixture.SeedSmartHomeActivity(cancellationToken);

        // Assert
        await collector.WaitForMeasurementsAsync(minCount: 1, cancellationToken: cancellationToken).WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);

        var temp = collector.GetMeasurementSnapshot();

        // Assert.Collection(collector.GetMeasurementSnapshot(),
        //     measurement =>
        //     {
        //         Assert.Equal("http", measurement.Tags["url.scheme"]);
        //         Assert.Equal("GET", measurement.Tags["http.request.method"]);
        //         Assert.Equal("/", measurement.Tags["http.route"]);
        //     });
    }
}
