// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.AutoScavenge.Domain;
using NCrontab;

namespace EventStore.AutoScavenge.Tests;

public static class Command {
	public static ICommand FastForward(TimeSpan time) => new SimulatorCommands.FastForward(time);
	public static ICommand ReceiveGossip(Guid nodeId, List<ClusterMember> members) =>
		new Commands.ReceiveGossip(new GossipMessage { NodeId = nodeId, Members = members });

	public static ICommand Configure(CrontabSchedule schedule, Action<Response<Unit>>? callback = null) =>
		new Commands.Configure(schedule, resp => callback?.Invoke(resp));

	public static ICommand Suspend { get; } = new SimulatorCommands.Suspend();

	public static ICommand PauseProcess(Action<Response<Unit>> callback) =>
		new Commands.PauseProcess(callback);

	public static ICommand ResumeProcess(Action<Response<Unit>> callback) =>
		new Commands.ResumeProcess(callback);

	public static ICommand GetStatus(TaskCompletionSource<Response<AutoScavengeStatusResponse>> source) =>
		new Commands.GetStatus(source.SetResult);
}
