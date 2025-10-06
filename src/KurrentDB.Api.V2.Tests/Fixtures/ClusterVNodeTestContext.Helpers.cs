// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#pragma warning disable CA1822 // Mark members as static

// ReSharper disable InconsistentNaming

using System.Runtime.CompilerServices;
using System.Text;
using Google.Protobuf.Collections;
using Grpc.Core;
using KurrentDB.Api.Streams;
using KurrentDB.Protocol.V2.Streams;
using KurrentDB.Testing.Sample.HomeAutomation;
using StreamRevision = KurrentDB.Api.Streams.StreamRevision;
using StreamsService = KurrentDB.Protocol.V2.Streams.StreamsService;

namespace KurrentDB.Api.Tests.Fixtures;

public static class StreamsClientExtensions {
    public static async ValueTask<AppendResponse> AppendAsync(this StreamsService.StreamsServiceClient client, AppendRequest request, CancellationToken cancellationToken) {
        using var session = client.AppendSession(cancellationToken: cancellationToken);
        await session.RequestStream.WriteAsync(request, cancellationToken);
        await session.RequestStream.CompleteAsync();
        var response = await session.ResponseAsync;
        return response.Output[0];
    }
}

public partial class ClusterVNodeTestContext {
    public async ValueTask<SeededSmartHomeActivity> SeedSmartHomeActivity(int numberOfEvents, CancellationToken cancellationToken) {
        var request  = HomeAutomationTestData.SimulateHomeActivity(numberOfEvents);
        var response = await StreamsClient.AppendAsync(request, cancellationToken: cancellationToken);
        return new(request, response.StreamRevision, response.HasPosition ? response.Position : -1);
    }

    public async ValueTask<SeededSmartHomeActivity> SeedSmartHomeActivity(CancellationToken cancellationToken) =>
        await SeedSmartHomeActivity(Random.Shared.Next(5, 15), cancellationToken);

    /// <summary>
    /// Creates a unique stream name for KurrentDB operations, combining the given category with a short unique identifier.
    /// </summary>
    /// <param name="category">The category associated with the stream name.</param>
    /// <returns>A StreamName instance containing the generated stream name.</returns>
    public StreamName NewStreamName([CallerMemberName] string category = "") => StreamName.From($"{category}-{TestUid.New()}");

    public CallCredentials CreateCallCredentials((string Username, string Password) credentials) {
        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{credentials.Username}:{credentials.Password}"));
        return CallCredentials.FromInterceptor((_, metadata) => {
            metadata.Add(new Metadata.Entry("Authorization", $"Basic {token}"));
            return Task.CompletedTask;
        });
    }

    public CallCredentials AdminCredentials   => CreateCallCredentials(("admin", "changeit"));
    public CallCredentials DefaultCredentials => CreateCallCredentials(default);
}

public record SeededSmartHomeActivity(SmartHomeActivity Activity, StreamRevision StreamRevision, long Position) {
    public SmartHome                   Home          => Activity.Home;
    public StreamName                  Stream        => Activity.Stream;
    public RepeatedField<AppendRecord> Records       => Activity.Records;
    public long                        LastTimestamp => Activity.LastTimestamp;

    public SmartHomeActivity SimulateMoreEvents(int? numberOfEvents = null) =>
        Activity.SimulateMoreEvents(numberOfEvents);

    public SmartHomeActivity WithExpectedRevision(long expectedRevision) =>
        Activity.WithExpectedRevision(expectedRevision);

    public static implicit operator AppendRequest(SeededSmartHomeActivity _) => _.Activity;
}
