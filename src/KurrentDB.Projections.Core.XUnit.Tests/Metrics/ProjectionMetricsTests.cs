// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics.Metrics;
using KurrentDB.Projections.Core.Metrics;
using KurrentDB.Projections.Core.Services;
using KurrentDB.Projections.Core.Services.Management;
using KurrentDB.Projections.Core.Services.Processing;
using Xunit;

namespace KurrentDB.Projections.Core.XUnit.Tests.Metrics;

public class ProjectionMetricsTests {
	private readonly ProjectionTracker _sut = new();

	public ProjectionMetricsTests() {
		_sut.OnNewStats([Stat("TestProjection", ProjectionMode.Continuous, CoreProjection.State.Running)]);
	}

	[Fact]
	public void ObserveEventsProcessed() {
		var measurements = _sut.ObserveEventsProcessed();
		var measurement = Assert.Single(measurements);
		AssertMeasurement(50L, ("projection", "TestProjection"))(measurement);
	}

	[Fact]
	public void ObserveRunning() {
		var measurements = _sut.ObserveRunning();
		var measurement = Assert.Single(measurements);
		AssertMeasurement(1L, ("projection", "TestProjection"))(measurement);
	}

	[Fact]
	public void ObserveProgress() {
		var measurements = _sut.ObserveProgress();
		var measurement = Assert.Single(measurements);
		AssertMeasurement(0.75f, ("projection", "TestProjection"))(measurement);
	}


	[Theory]
	[MemberData(nameof(AllStatuses))]
	public void ObservedStatuses(StatusCombination combo) {
		_sut.OnNewStats([Stat(combo.Projection, ProjectionMode.Continuous, combo.WhenObservedStateIs)]);
		var measurements = _sut.ObserveStatus();
		Assert.Collection(measurements,
			AssertMeasurement(combo.ThenRunningIs, ("projection", combo.Projection), ("status", "Running")),
			AssertMeasurement(combo.ThenFaultedIs, ("projection", combo.Projection), ("status", "Faulted")),
			AssertMeasurement(combo.ThenStoppedIs, ("projection", combo.Projection), ("status", "Stopped"))
		);
	}



	static Action<Measurement<T>> AssertMeasurement<T>(
		T expectedValue, params (string, string?)[] tags) where T : struct =>

		actualMeasurement => {
			Assert.Equal(expectedValue, actualMeasurement.Value);
			if (actualMeasurement.Tags == null) return;
			Assert.Equal(
				tags,
				actualMeasurement.Tags.ToArray().Select(tag => (tag.Key, tag.Value as string)));
		};


	public record StatusCombination (string Projection, CoreProjection.State WhenObservedStateIs, long ThenRunningIs, long ThenFaultedIs, long ThenStoppedIs);

	public static IEnumerable<object[]> AllStatuses() {

		foreach (var state in Enum.GetValues<CoreProjection.State>()) {
			var projectionName = $"Test-{state}";
			switch (state) {
				case CoreProjection.State.Running:
					yield return [new StatusCombination(projectionName,state, 1, 0, 0)];
					continue;
				case CoreProjection.State.Faulted:
					yield return [new StatusCombination(projectionName,state, 0, 1, 0)];
					continue;
				case CoreProjection.State.Stopped:
					yield return [new StatusCombination(projectionName, state, 0, 0, 1)];
					continue;

				case CoreProjection.State.Initial:
				case CoreProjection.State.LoadStateRequested:
				case CoreProjection.State.StateLoaded:
				case CoreProjection.State.Subscribed:
				case CoreProjection.State.Stopping:
				case CoreProjection.State.FaultedStopping:
				case CoreProjection.State.CompletingPhase:
				case CoreProjection.State.PhaseCompleted:
				case CoreProjection.State.Suspended:
				default:
					yield return [new StatusCombination(projectionName, state, 0, 0, 0)];
					break;
			}
		}
	}

	static ProjectionStatistics Stat(string name, ProjectionMode mode, CoreProjection.State state) =>
		new() {
			Name = name,
			ProjectionId = 1234,
			Epoch = -1,
			Version = -1,
			Mode = mode,
			Status = state.ToString(),
			Progress = 75,
			EventsProcessedAfterRestart = 50,
		};

}
