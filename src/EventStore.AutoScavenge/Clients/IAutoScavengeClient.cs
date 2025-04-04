// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.AutoScavenge.Domain;
using NCrontab;

namespace EventStore.AutoScavenge.Clients;

// Interface to manage the auto scavenge.
public interface IAutoScavengeClient {
	Task<Response<AutoScavengeStatusResponse>> GetStatus(CancellationToken token);
	Task<Response<Unit>> Pause(CancellationToken token);
	Task<Response<Unit>> Resume(CancellationToken token);
	Task<Response<Unit>> Configure(CrontabSchedule schedule, CancellationToken token);

	public static IAutoScavengeClient None => NoAutoScavengeClient.Instance;
}

file class NoAutoScavengeClient : IAutoScavengeClient {
	public static NoAutoScavengeClient Instance { get; } = new NoAutoScavengeClient();

	private const string Error = "No auto scavenge client";

	readonly Task<Response<AutoScavengeStatusResponse>> _statusResponse =
		Task.FromResult(Response.ServerError<AutoScavengeStatusResponse>(Error));

	readonly Task<Response<Unit>> _unitResponse =
		Task.FromResult(Response.ServerError(Error));

	public Task<Response<Unit>> Configure(CrontabSchedule schedule, CancellationToken token) =>
		_unitResponse;

	public Task<Response<AutoScavengeStatusResponse>> GetStatus(CancellationToken token) =>
		_statusResponse;

	public Task<Response<Unit>> Pause(CancellationToken token) =>
		_unitResponse;

	public Task<Response<Unit>> Resume(CancellationToken token) =>
		_unitResponse;
}
