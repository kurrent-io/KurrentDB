// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json;
using Google.Protobuf;
using Google.Protobuf.Collections;
using Google.Protobuf.WellKnownTypes;
using KurrentDB.Api.Streams;
using KurrentDB.Protocol.V2.Streams;
using KurrentDB.Testing.Sample.HomeAutomation;

using SchemaFormat = KurrentDB.Protocol.V2.Streams.SchemaFormat;
using SchemaInfo   = KurrentDB.Protocol.V2.Streams.SchemaInfo;

namespace KurrentDB.Api.Tests.Fixtures;

public static class HomeAutomationTestData {
    public static List<SmartHomeActivity> SimulateHousingComplexActivity(int homes = 3, int eventsPerHome = 100) {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(homes, nameof(homes));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(eventsPerHome, nameof(eventsPerHome));

        return HomeAutomationDataSet.Default
            .Homes(homes)
            .Select(home => SimulateHomeActivity(home, eventsPerHome))
            .ToList();
    }

    public static SmartHomeActivity SimulateHomeActivity(SmartHome home, int? numberOfEvents = null, long? startTime = null) {
        var events  = HomeAutomationDataSet.Default.Events(home, numberOfEvents ?? Random.Shared.Next(5, 15), startTime);
        var records = events.Aggregate(new List<AppendRecord>(), (seed, evt) => {
            seed.Add(CreateRecord(evt, seed.Count + 1));
            return seed;
        });

        return new(
            home,
            new AppendRequest {
                Stream  = $"{nameof(SmartHomeActivity)}-{home.Id}",
                Records = { records }
            }
        );
    }

    public static SmartHomeActivity SimulateHomeActivity(int? numberOfEvents = null, long? startTime = null) =>
        SimulateHomeActivity(HomeAutomationDataSet.Default.Home(), numberOfEvents, startTime);

    static AppendRecord CreateRecord(object evt, int sequence) {
        dynamic iotEvent = evt;

        var recordId  = iotEvent.EventId.ToString();
        var timestamp = iotEvent.Timestamp;

        var record = new AppendRecord {
            RecordId = recordId,
            Data     = UnsafeByteOperations.UnsafeWrap(JsonSerializer.SerializeToUtf8Bytes(evt)),
            Schema   = new SchemaInfo {
                Name   = evt.GetType().Name,
                Format = SchemaFormat.Json,
            },
            Properties = {
                { "tests.iot.event-sequence", Value.ForNumber(sequence) }
            },
            Timestamp = timestamp
        };

        return record;
    }
}

public record SmartHomeActivity(SmartHome Home, AppendRequest AppendRequest) {
    public StreamName                  Stream        => AppendRequest.Stream;
    public RepeatedField<AppendRecord> Records       => AppendRequest.Records;
    public long                        LastTimestamp => AppendRequest.Records.Last().Timestamp;

    public SmartHomeActivity SimulateMoreEvents(int? numberOfEvents = null) =>
        this with { AppendRequest = HomeAutomationTestData.SimulateHomeActivity(Home, numberOfEvents, LastTimestamp) };

    public SmartHomeActivity WithExpectedRevision(long expectedRevision) =>
        this with { AppendRequest = AppendRequest.With(r => r.ExpectedRevision = expectedRevision) };

    public static implicit operator AppendRequest(SmartHomeActivity _) => _.AppendRequest;
}
