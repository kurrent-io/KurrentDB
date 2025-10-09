// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics.Metrics;
using KurrentDB.Projections.Core.Metrics;
using KurrentDB.Projections.Core.Services;
using KurrentDB.Projections.Core.Services.Management;
using Xunit;

namespace KurrentDB.Projections.Core.XUnit.Tests.Metrics;

public class ProjectionTrackerTests {
	private readonly ProjectionTracker _sut = new();

	public ProjectionTrackerTests() {
		_sut.OnNewStats([Stat("TestProjection", ProjectionMode.Continuous, ManagedProjectionState.Running, stateSizes: null)]);
	}

	[Fact]
	public void ObserveEventsProcessed() {
		var measurements = _sut.ObserveEventsProcessed();
		var measurement = Assert.Single(measurements);
		AssertMeasurement(50L, new KeyValuePair<string, object>("projection", "TestProjection"))(measurement);
	}

	[Fact]
	public void ObserveRunning() {
		var measurements = _sut.ObserveRunning();
		var measurement = Assert.Single(measurements);
		AssertMeasurement(1L, new KeyValuePair<string, object>("projection", "TestProjection"))(measurement);
	}

	[Fact]
	public void ObserveProgress() {
		var measurements = _sut.ObserveProgress();
		var measurement = Assert.Single(measurements);
		AssertMeasurement(0.75f, new KeyValuePair<string, object>("projection", "TestProjection"))(measurement);
	}


	[Theory]
	[MemberData(nameof(AllStatuses))]
	public void ObservedStatuses(StatusCombination combo) {
		_sut.OnNewStats([Stat(combo.Projection, ProjectionMode.Continuous, combo.WhenObservedStateIs, stateSizes: null)]);
		var measurements = _sut.ObserveStatus();
		var prj = new KeyValuePair<string, object>("projection", combo.Projection);
		Assert.Collection(measurements,
			AssertMeasurement(combo.ThenRunningIs, prj, ProjectionTracker.StatusRunning),
			AssertMeasurement(combo.ThenFaultedIs, prj, ProjectionTracker.StatusFaulted),
			AssertMeasurement(combo.ThenStoppedIs, prj, ProjectionTracker.StatusStopped)
		);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void ObserveStateSizes(bool isNull) {
		var stateSizes = isNull ? null : new Dictionary<string, int> {
			[string.Empty] = 10,
			["test-partition"] = 12,
		};

		_sut.OnNewStats([Stat("TestProjection", ProjectionMode.Continuous, ManagedProjectionState.Running, stateSizes: stateSizes)]);

		var measurements = _sut.ObserveStateSize();

		if (isNull) {
			Assert.Empty(measurements);
		} else {
			var ms = measurements.ToArray();
			Assert.Equal(2, ms.Length);
			AssertMeasurement(10, new KeyValuePair<string, object>("projection", "TestProjection"))(ms[0]);
			AssertMeasurement(12, new KeyValuePair<string, object>("projection", "TestProjection"),
				new KeyValuePair<string, object>("partition", "test-partition"))(ms[1]);
		}
	}

	[Fact]
	public void ObserveStateSizeBounds() {
		_sut.OnNewStats([Stat("TestProjection", ProjectionMode.Continuous, ManagedProjectionState.Running, stateSizes: null)]);

		var measurements = _sut.ObserveStateSizeBound();

		var ms = measurements.ToArray();
		Assert.Equal(2, ms.Length);
		AssertMeasurement(500, new KeyValuePair<string, object>("bound", "THRESHOLD"))(ms[0]);
		AssertMeasurement(900, new KeyValuePair<string, object>("bound", "LIMIT"))(ms[1]);
	}

	static Action<Measurement<T>> AssertMeasurement<T>(T expectedValue, params KeyValuePair<string, object>[] tags)
		where T : struct =>

		actualMeasurement => {
			Assert.Equal(expectedValue, actualMeasurement.Value);
			if (actualMeasurement.Tags == null)
				return;
			var actualTags = actualMeasurement.Tags.ToArray();
			Assert.Equal(tags, actualTags!, (a, b) => a.Key == b.Key && a.Value.Equals(b.Value));
		};


	public record StatusCombination(
		string Projection,
		ManagedProjectionState WhenObservedStateIs,
		long ThenRunningIs,
		long ThenFaultedIs,
		long ThenStoppedIs);

	public static IEnumerable<object[]> AllStatuses() {

		foreach (var state in Enum.GetValues<ManagedProjectionState>()) {
			var projectionName = $"Test-{state}";
			switch (state) {
				case ManagedProjectionState.Creating:
				case ManagedProjectionState.Loading:
				case ManagedProjectionState.Loaded:
				case ManagedProjectionState.Preparing:
				case ManagedProjectionState.Prepared:
				case ManagedProjectionState.Starting:
				case ManagedProjectionState.LoadingStopped:
				case ManagedProjectionState.Stopping:
				case ManagedProjectionState.Completed:
				case ManagedProjectionState.Aborted:
				case ManagedProjectionState.Deleting:
				case ManagedProjectionState.Aborting:
					yield return [new StatusCombination(projectionName, state, 0, 0, 0)];
					break;
				case ManagedProjectionState.Running:
					yield return [new StatusCombination(projectionName, state, 1, 0, 0)];
					break;
				case ManagedProjectionState.Stopped:
					yield return [new StatusCombination(projectionName, state, 0, 0, 1)];
					break;
				case ManagedProjectionState.Faulted:
					yield return [new StatusCombination(projectionName, state, 0, 1, 0)];
					break;
				default:
					throw new ArgumentOutOfRangeException();
			}
		}
	}

	private static ProjectionStatistics Stat(string name, ProjectionMode mode, ManagedProjectionState state, Dictionary<string, int>? stateSizes) =>
		new() {
			Name = name,
			ProjectionId = 1234,
			Epoch = -1,
			Version = -1,
			Mode = mode,
			Status = state.ToString(),
			Progress = 75,
			EventsProcessedAfterRestart = 50,
			StateSizes = stateSizes,
			StateSizeThreshold = 500,
			StateSizeLimit = 900,
		};
}
