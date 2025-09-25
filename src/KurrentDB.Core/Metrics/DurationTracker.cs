// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading.Tasks;

namespace KurrentDB.Core.Metrics;

public interface IDurationTracker {
	Duration Start();
}

public class DurationTracker : IDurationTracker {
	private readonly DurationMetric _metric;
	private readonly string _durationName;

	public DurationTracker(DurationMetric metric, string durationName) {
		_metric = metric;
		_durationName = durationName;
	}

	public Duration Start() => _metric.Start(_durationName);

	public class NoOp : IDurationTracker {
		public Duration Start() => Duration.Nil;
	}
}

public static class DurationTrackerExtensions {
    /// <summary>
    /// Records the duration of an asynchronous action, marking it as failed if an exception is thrown.
    /// </summary>
    public static async Task<T> Record<T>(this IDurationTracker recorder, Func<Task<T>> action) {
        using var duration = recorder.Start();
        try {
            return await action();
        }
        catch (Exception) {
            duration.Failed();
            throw;
        }
    }

    /// <summary>
    /// Records the duration of an asynchronous action with state, marking it as failed if an exception is thrown.
    /// </summary>
    public static async Task<T> Record<T, TState>(this IDurationTracker recorder, Func<TState, Task<T>> action, TState state) {
        using var duration = recorder.Start();
        try {
            return await action(state);
        }
        catch (Exception) {
            duration.Failed();
            throw;
        }
    }

    /// <summary>
    /// Records the duration of an asynchronous action with state, marking it as failed if an exception is thrown.
    /// </summary>
    public static async Task Record<TState>(this IDurationTracker recorder, Func<TState, Task> action, TState state) {
        using var duration = recorder.Start();
        try {
            await action(state);
        }
        catch (Exception) {
            duration.Failed();
            throw;
        }
    }
}
