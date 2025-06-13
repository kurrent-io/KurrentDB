// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#pragma warning disable TCS001 // TaskCompletionSource construction should use TaskCompletionSourceFactory

using System.Threading.Tasks;

namespace KurrentDB.Common.Utils;
public static class TaskCompletionSourceFactory {
	// CreateSafe always applies RunContinuationsAsynchronously.
	public static TaskCompletionSource CreateSafe() =>
		new(TaskCreationOptions.RunContinuationsAsynchronously);

	public static TaskCompletionSource CreateSafe(object state) =>
		new(state, TaskCreationOptions.RunContinuationsAsynchronously);

	public static TaskCompletionSource CreateSafe(TaskCreationOptions options) =>
		new(TaskCreationOptions.RunContinuationsAsynchronously | options);

	public static TaskCompletionSource CreateSafe(object state, TaskCreationOptions options) =>
		new(state, TaskCreationOptions.RunContinuationsAsynchronously | options);

	public static TaskCompletionSource<T> CreateSafe<T>() =>
		new(TaskCreationOptions.RunContinuationsAsynchronously);

	public static TaskCompletionSource<T> CreateSafe<T>(object state) =>
		new(state, TaskCreationOptions.RunContinuationsAsynchronously);

	public static TaskCompletionSource<T> CreateSafe<T>(TaskCreationOptions options) =>
		new(TaskCreationOptions.RunContinuationsAsynchronously | options);

	public static TaskCompletionSource<T> CreateSafe<T>(object state, TaskCreationOptions options) =>
		new(state, TaskCreationOptions.RunContinuationsAsynchronously | options);

	// CreateManual leaves the behavior up to the caller.
	public static TaskCompletionSource CreateManual() =>
		new();

	public static TaskCompletionSource CreateManual(object state) =>
		new(state);

	public static TaskCompletionSource CreateManual(TaskCreationOptions options) =>
		new(options);

	public static TaskCompletionSource CreateManual(object state, TaskCreationOptions options) =>
		new(state, options);

	public static TaskCompletionSource<T> CreateManual<T>() =>
		new();

	public static TaskCompletionSource<T> CreateManual<T>(object state) =>
		new(state);

	public static TaskCompletionSource<T> CreateManual<T>(TaskCreationOptions options) =>
		new(options);

	public static TaskCompletionSource<T> CreateManual<T>(object state, TaskCreationOptions options) =>
		new(state, options);

	// CreateDefault is temporary, for callers that haven't been analysed so that we can try out setting them all to async at once.
	private const bool SafeByDefault = true;

	public static TaskCompletionSource CreateDefault() =>
		SafeByDefault ? CreateSafe() : CreateManual();

	public static TaskCompletionSource CreateDefault(object state) =>
		SafeByDefault ? CreateSafe(state) : CreateManual(state);

	public static TaskCompletionSource CreateDefault(TaskCreationOptions options) =>
		SafeByDefault ? CreateSafe(options) : CreateManual(options);

	public static TaskCompletionSource CreateDefault(object state, TaskCreationOptions options) =>
		SafeByDefault ? CreateSafe(state, options) : CreateManual(state, options);

	public static TaskCompletionSource<T> CreateDefault<T>() =>
		SafeByDefault ? CreateSafe<T>() : CreateManual<T>();

	public static TaskCompletionSource<T> CreateDefault<T>(object state) =>
		SafeByDefault ? CreateSafe<T>(state) : CreateManual<T>(state);

	public static TaskCompletionSource<T> CreateDefault<T>(TaskCreationOptions options) =>
		SafeByDefault ? CreateSafe<T>(options) : CreateManual<T>(options);

	public static TaskCompletionSource<T> CreateDefault<T>(object state, TaskCreationOptions options) =>
		SafeByDefault ? CreateSafe<T>(state, options) : CreateManual<T>(state, options);
}
