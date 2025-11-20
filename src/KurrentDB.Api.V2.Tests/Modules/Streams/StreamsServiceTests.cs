// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable AccessToDisposedClosure

using System.Reflection;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Google.Rpc;
using Grpc.Core;
using Humanizer;
using KurrentDB.Api.Tests.Fixtures;
using KurrentDB.Protocol.V2.Streams;
using KurrentDB.Protocol.V2.Streams.Errors;
using KurrentDB.Testing.Bogus;
using Microsoft.Extensions.Logging;

namespace KurrentDB.Api.Tests.Streams;

public class StreamsServiceTests {
    [ClassDataSource<ClusterVNodeTestContext>(Shared = SharedType.PerTestSession)]
    public required ClusterVNodeTestContext Fixture { get; [UsedImplicitly] init; }

    [ClassDataSource<BogusFaker>(Shared = SharedType.PerTestSession)]
    public required BogusFaker Faker { get; [UsedImplicitly] init; }

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

        await Assert.That(rex.StatusCode).IsEqualTo(StatusCode.FailedPrecondition);
        await Assert.That(details).IsNotNull();
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

        await Assert.That(rex.StatusCode).IsEqualTo(StatusCode.InvalidArgument);
        await Assert.That(details).IsNotNull();
    }

    [Test]
    public async ValueTask append_session_throws_when_no_requests_are_sent(CancellationToken cancellationToken) {
        // Act
        using var session = Fixture.StreamsClient.AppendSession(cancellationToken: cancellationToken);
        await session.RequestStream.CompleteAsync();

        // ReSharper disable once AccessToDisposedClosure
        var appendTask = async () => await session.ResponseAsync;

        // Assert
        var rex     = await appendTask.ShouldThrowAsync<RpcException>();
        var errorInfo = rex.GetRpcStatus()?.GetDetail<ErrorInfo>();

        await Assert.That(rex.StatusCode).IsEqualTo(StatusCode.FailedPrecondition);
        await Assert.That(errorInfo).IsNotNull();

        await Assert.That(errorInfo!.Reason).IsEqualTo("APPEND_SESSION_NO_REQUESTS");

        var temp = GetEnumOriginalName(StreamsError.AppendSessionNoRequests);

        return;

        static string GetEnumOriginalName(object enumValue) =>
            enumValue.GetType().GetField(enumValue.ToString()!)!
                .GetCustomAttribute<OriginalNameAttribute>()!.Name;

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

        await Assert.That(rex.StatusCode).IsEqualTo(StatusCode.Aborted);
        await Assert.That(details).IsNotNull();
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
    [Repeat(10)]
    public async ValueTask append_session_throws_when_record_is_larger_than_configured_limit(CancellationToken cancellationToken) {
        // Arrange
        var request = HomeAutomationTestData.SimulateHomeActivity(1);

        var recordSize = (int)(Fixture.ServerOptions.Application.MaxAppendEventSize * Faker.Random.Double(1.01, 1.04));

        request.Records[0].Data = UnsafeByteOperations.UnsafeWrap(Faker.Random.Bytes(recordSize));

        var appendTask = async () => await Fixture.StreamsClient.AppendAsync(request, cancellationToken: cancellationToken);

        // Act & Assert
        var rex     = await appendTask.ShouldThrowAsync<RpcException>();
        var details = rex.GetRpcStatus()?.GetDetail<AppendRecordSizeExceededErrorDetails>();

        await Assert.That(rex.StatusCode).IsEqualTo(StatusCode.InvalidArgument);
        await Assert.That(details).IsNotNull();
    }

    [Test]
    public async ValueTask append_session_throws_when_record_is_larger_than_max_receive_message_size(CancellationToken cancellationToken) {
        // Arrange
        var request = HomeAutomationTestData.SimulateHomeActivity(1);

        var recordSize = Fixture.ServerOptions.Application.MaxAppendEventSize * 2;

        request.Records[0].Data = UnsafeByteOperations.UnsafeWrap(Faker.Random.Bytes(recordSize));

        var appendTask = async () => await Fixture.StreamsClient.AppendAsync(request, cancellationToken: cancellationToken);

        // Act & Assert
        var rex = await appendTask.ShouldThrowAsync<RpcException>();
        await Assert.That(rex.StatusCode).IsEqualTo(StatusCode.InvalidArgument);
    }

    [Test]
    [Repeat(50)]
    public async ValueTask append_session_throws_when_transaction_is_too_large(CancellationToken cancellationToken) {
        // Arrange

        // Calculate a random max request size larger than max append size but smaller
        // than max append size * 1.5 to ensure we do not hit max receive size limit
        var targetMaxAppendSize = (int)(Fixture.ServerOptions.Application.MaxAppendSize * Faker.Random.Double(1.10, 1.49));

        Fixture.Logger.LogInformation(
            "Target Request Size: {MaxRequestSize} (MaxAppendEventSize: {MaxAppendEventSize} | MaxAppendSize: {MaxAppendSize})",
            targetMaxAppendSize.Bytes().Humanize("0.000"),
            Fixture.ServerOptions.Application.MaxAppendEventSize.Bytes().Humanize("0"),
            Fixture.ServerOptions.Application.MaxAppendSize.Bytes().Humanize("0"));

        var request = new AppendRequest {
            Stream  = $"{nameof(SmartHomeActivity)}-{Guid.NewGuid():N}"
        };

        // Fill the request with records and must ensure that the size of the request must be within the limits
        while (true) {
            // Create a random valid record size
            var validRecordSize = Faker.Random.Int(
                Math.Min(Fixture.ServerOptions.Application.MaxAppendSize / 4, 1024),
                Fixture.ServerOptions.Application.MaxAppendEventSize);

            request.Records.Add(CreateSyntheticTestRecord(Faker.Random.Bytes(validRecordSize)));

            var requestSize = request.CalculateSize();
            if (requestSize > targetMaxAppendSize) {
                var exceededBy = requestSize - targetMaxAppendSize;
                var dataSize   = Math.Max(1, validRecordSize - exceededBy);

                request.Records[^1].Data = UnsafeByteOperations.UnsafeWrap(Faker.Random.Bytes(dataSize));

                Fixture.Logger.LogDebug(
                    "Adjusted last record size from {OriginalSize} to {NewSize} to fit within target max append size of {TargetMaxAppendSize}",
                    validRecordSize.Bytes().Humanize("0.000"),
                    dataSize.Bytes().Humanize("0.000"),
                    targetMaxAppendSize.Bytes().Humanize("0.000"));

                break;
            }

            Fixture.Logger.LogTrace(
                "Added record of size {RecordSize}, total request size is now {RequestSize} with {Records} records",
                validRecordSize.Bytes().Humanize("0.000"),
                requestSize.Bytes().Humanize("0.000"),
                request.Records.Count);
        }

        Fixture.Logger.LogInformation(
            "Created request with {Records} records and a calculated total size of {Size}",
            request.Records.Count, request.CalculateSize().Bytes().Humanize("0.000"));

        using var session = Fixture.StreamsClient.AppendSession(cancellationToken: cancellationToken);
        await session.RequestStream.WriteAsync(request, cancellationToken);
        await session.RequestStream.CompleteAsync();

        // ReSharper disable once AccessToDisposedClosure
        var appendTask = async () => await session.ResponseAsync;

        // Act & Assert
        var rex     = await appendTask.ShouldThrowAsync<RpcException>();
        var details = rex.GetRpcStatus()?.GetDetail<AppendTransactionSizeExceededErrorDetails>();

        await Assert.That(rex.StatusCode).IsEqualTo(StatusCode.Aborted);
        await Assert.That(details).IsNotNull();

        return;

        AppendRecord CreateSyntheticTestRecord(Memory<byte> data) =>
            new() {
                RecordId = Guid.NewGuid().ToString(),
                Data     = UnsafeByteOperations.UnsafeWrap(data),
                Schema   = new SchemaInfo {
                    Name   = "SyntheticTestRecord.V1",
                    Format = SchemaFormat.Bytes
                }
            };
    }

    [Test, Skip("Skipping for now.")]
    public async ValueTask append_session_throws_when_user_is_not_authenticated(CancellationToken cancellationToken) {
        // Arrange
        var callOptions = new CallOptions(
            credentials: Fixture.CreateCallCredentials(("invalid", "credentials")),
            cancellationToken: cancellationToken);

        // Act
        using var session = Fixture.StreamsClient.AppendSession(callOptions);

        var appendTask = async () => await session.ResponseAsync;

        // Assert
        var rex = await appendTask.ShouldThrowAsync<RpcException>();
        await Assert.That(rex.StatusCode).IsEqualTo(StatusCode.Unauthenticated);
    }

    [Test, Skip("Skipping for now.")]
    public ValueTask append_session_throws_when_user_does_not_have_permissions(CancellationToken cancellationToken) {
        throw new NotImplementedException();
    }
}
