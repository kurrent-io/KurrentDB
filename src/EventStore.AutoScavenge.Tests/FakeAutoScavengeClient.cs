// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.AutoScavenge.Clients;
using EventStore.AutoScavenge.Domain;
using NCrontab;

namespace EventStore.AutoScavenge.Tests;

public class FakeAutoScavengeClient : IAutoScavengeClient {
	public ICommand? Command { get; private set; }

	public Task<Response<AutoScavengeStatusResponse>> GetStatus(CancellationToken token) {
		Command = new Commands.GetStatus(_ => {});
		return Task.FromResult(Response.Successful(new AutoScavengeStatusResponse(AutoScavengeStatusResponse.Status.NotConfigured, null, null)));
	}

	public Task<Response<Unit>> Pause(CancellationToken token) {
		Command = new Commands.PauseProcess(_ => { });
		return Task.FromResult(Response.Accepted());
	}

	public Task<Response<Unit>> Resume(CancellationToken token) {
		Command = new Commands.ResumeProcess(_ => { });
		return Task.FromResult(Response.Accepted());
	}

	public Task<Response<Unit>> Configure(CrontabSchedule schedule, CancellationToken token) {
		Command = new Commands.Configure(schedule, _ => { });
		return Task.FromResult(Response.Accepted());
	}
}
