// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#pragma warning disable CA1822 // Mark members as static

// ReSharper disable InconsistentNaming

using System.Runtime.CompilerServices;
using Google.Protobuf.Collections;
using KurrentDB.Api.Streams;
using KurrentDB.Protocol.V2.Streams;
using KurrentDB.Testing.Sample.HomeAutomation;
using StreamRevision = KurrentDB.Api.Streams.StreamRevision;

namespace KurrentDB.Api.Tests.Fixtures;

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

// public record SeededHousingComplexActivity(IReadOnlyList<SeededSmartHomeActivity> Activity, long Position) {
//     public int TotalHomes  => Activity.Count;
//     public int TotalEvents => Activity.Sum(a => a.Records.Count);
//
//     public IEnumerable<StreamName> Streams() => Activity.Select(a => a.Stream);
// }

public partial class ClusterVNodeTestContext {
    public async ValueTask<SeededSmartHomeActivity> SeedSmartHomeActivity(int numberOfEvents, CancellationToken cancellationToken) {
        var request  = HomeAutomationTestData.SimulateHomeActivity(numberOfEvents);
        var response = await StreamsClient.AppendAsync(request, cancellationToken: cancellationToken);
        return new(request, response.StreamRevision, response.HasPosition ? response.Position : -1);
    }

    public async ValueTask<SeededSmartHomeActivity> SeedSmartHomeActivity(CancellationToken cancellationToken) =>
        await SeedSmartHomeActivity(Random.Shared.Next(5, 15), cancellationToken);

    // public async ValueTask<SeededHousingComplexActivity> SeedHousingComplexActivity(int homes, int eventsPerHome, CancellationToken cancellationToken) {
    //     var requests = HomeAutomationTestData
    //         .SimulateHousingComplexActivity(homes, eventsPerHome);
    //
    //     // Act
    //     Logger.LogInformation(
    //         "Seeding smart home activity for append session for {Homes} homes with a total of {Events} events",
    //         homes, homes * eventsPerHome);
    //
    //     using var session = StreamsClient.AppendSession(cancellationToken: cancellationToken);
    //
    //     foreach (var request in requests) {
    //         Logger.LogInformation("Appending {Count} records to stream {Stream}", request.Records.Count, request.Stream);
    //         await session.RequestStream.WriteAsync(request, cancellationToken);
    //     }
    //
    //     await session.RequestStream.CompleteAsync();
    //
    //     var response = await session.ResponseAsync;
    //
    //     Logger.LogInformation("Seeding smart home activity seeded at position {Position}", response.Position);
    //
    //     return new(requests.Se response.Output.StreamRevision, response.Position);
    // }

    /// <summary>
    /// Creates a unique stream name for KurrentDB operations, combining the given category with a short unique identifier.
    /// </summary>
    /// <param name="category">The category associated with the stream name.</param>
    /// <returns>A StreamName instance containing the generated stream name.</returns>
    public StreamName NewStreamName([CallerMemberName] string category = "") => StreamName.From($"{category}-{TestUid.New()}");
}
