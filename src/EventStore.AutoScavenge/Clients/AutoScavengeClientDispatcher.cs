// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.AutoScavenge.Domain;
using NCrontab;

namespace EventStore.AutoScavenge.Clients;

// Manages the autoscavenge locally or remotely depending on whether we are leader
public class AutoScavengeClientDispatcher(
	InternalAutoScavengeClient internalClient,
	ProxyAutoScavengeClient proxyClient) : IGossipAware, IAutoScavengeClient {

	public Task<Response<AutoScavengeStatusResponse>> GetStatus(CancellationToken token) {
		return Target.GetStatus(token);
	}

	public Task<Response<Unit>> Pause(CancellationToken token) {
		return Target.Pause(token);
	}

	public Task<Response<Unit>> Resume(CancellationToken token) {
		return Target.Resume(token);
	}

	public Task<Response<Unit>> Configure(CrontabSchedule schedule, CancellationToken token) {
		return Target.Configure(schedule, token);
	}

	public void ReceiveGossipMessage(GossipMessage msg) {
		proxyClient.ReceiveGossipMessage(msg);
	}

	private bool IsLeader() {
		return proxyClient.TryGetCurrentLeader(out _, out var isSelf) && isSelf;
	}

	private IAutoScavengeClient Target => IsLeader() ? internalClient : proxyClient;
}
