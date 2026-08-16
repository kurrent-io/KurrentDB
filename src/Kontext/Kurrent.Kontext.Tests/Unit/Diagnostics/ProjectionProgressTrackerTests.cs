// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics.Metrics;
using Kurrent.Kontext.Diagnostics;
using KurrentDB.Testing.Bogus;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Kurrent.Kontext.Tests;

[Category("Diagnostics")]
public class ProjectionProgressTrackerTests {
    const string ServiceName      = "kontext-test";
    const string Scope            = "projections";
    const string GapInstrument    = $"{ServiceName}.{Scope}.gap";
    const string LagInstrument    = $"{ServiceName}.{Scope}.lag";
    const string CommitInstrument = $"{ServiceName}.{Scope}.commit.seconds";

    [ClassDataSource<BogusFaker>(Shared = SharedType.PerTestSession)]
    public required BogusFaker Faker { get; init; }

    [Test]
    public async ValueTask commit_scope_records_elapsed_seconds_in_histogram() {
        // Arrange
        var expectedSeconds = Faker.Random.Double(0.1, 5.0);
        var tagKey          = Faker.Random.AlphaNumeric(8);
        var name            = Faker.Random.AlphaNumeric(8);
        var expectedTag     = new KeyValuePair<string, object?>(tagKey, name);

        using var meter = new Meter(Faker.Random.AlphaNumeric(12));
        var clock   = new FakeTimeProvider();
        var tracker = CreateTracker(meter, clock, tagKey, name, () => ProgressMark.Unset);

        List<(double Value, KeyValuePair<string, object?>[] Tags)> measurements = [];
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) => {
            if (instrument.Meter == meter && instrument.Name == CommitInstrument)
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<double>((_, value, tags, _) => measurements.Add((value, tags.ToArray())));
        listener.Start();

        // Act
        using (tracker.StartCommit())
            clock.Advance(TimeSpan.FromSeconds(expectedSeconds));

        // Assert
        await Assert.That(measurements).HasSingleItem();
        await Assert.That(measurements[0].Value).IsEqualTo(expectedSeconds).Within(0.000_001);
        await Assert.That(measurements[0].Tags).Contains(expectedTag);
    }

    [Test]
    public async ValueTask gauges_report_gap_and_lag_between_head_and_processed() {
        // Arrange
        var headTimestamp = Faker.Date.Recent();
        var gapBytes      = Faker.Random.Long(1, 50_000);
        var lagSpan       = TimeSpan.FromSeconds(Faker.Random.Int(1, 300));
        var processedPos  = Faker.Random.Long(1_000, 100_000);

        var head               = new ProgressMark(processedPos + gapBytes, headTimestamp);
        var processed          = new ProgressMark(processedPos, headTimestamp - lagSpan);
        var expectedLagSeconds = lagSpan.TotalSeconds;

        using var meter = new Meter(Faker.Random.AlphaNumeric(12));
        var tracker = CreateTracker(meter, new FakeTimeProvider(), Faker.Random.AlphaNumeric(8), Faker.Random.AlphaNumeric(8), () => head);

        List<long> gaps   = [];
        List<double> lags = [];
        using var listener = ListenToGauges(meter, gaps, lags);

        tracker.RecordProcessed(processed);

        // Act
        listener.RecordObservableInstruments();

        // Assert
        await Assert.That(gaps).HasSingleItem();
        await Assert.That(gaps[0]).IsEqualTo(gapBytes);
        await Assert.That(lags).HasSingleItem();
        await Assert.That(lags[0]).IsEqualTo(expectedLagSeconds).Within(0.000_001);
    }

    [Test]
    public async ValueTask gauges_stay_silent_until_first_processed_mark() {
        // Arrange
        var head = new ProgressMark(Faker.Random.Long(1_000, 100_000), Faker.Date.Recent());

        using var meter = new Meter(Faker.Random.AlphaNumeric(12));
        _ = CreateTracker(meter, new FakeTimeProvider(), Faker.Random.AlphaNumeric(8), Faker.Random.AlphaNumeric(8), () => head);

        List<long> gaps   = [];
        List<double> lags = [];
        using var listener = ListenToGauges(meter, gaps, lags);

        // Act
        listener.RecordObservableInstruments();

        // Assert
        await Assert.That(gaps).IsEmpty();
        await Assert.That(lags).IsEmpty();
    }

    static ProjectionProgressTracker CreateTracker(Meter meter, TimeProvider clock, string tagKey, string name, Func<ProgressMark> getHead) =>
        new(ServiceName, new() { Scope = Scope, TagKey = tagKey, Name = name, GetHead = getHead }, meter, clock, NullLogger<ProjectionProgressTracker>.Instance);

    static MeterListener ListenToGauges(Meter meter, List<long> gaps, List<double> lags) {
        var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) => {
            if (instrument.Meter == meter && instrument.Name is GapInstrument or LagInstrument)
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) => gaps.Add(value));
        listener.SetMeasurementEventCallback<double>((_, value, _, _) => lags.Add(value));
        listener.Start();
        return listener;
    }
}
