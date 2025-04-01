// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.AutoScavenge.Domain;
using NCrontab;

namespace EventStore.AutoScavenge;

public class Commands {
	public record Assess() : Command<Unit>(_ => { }) {
		public static Assess Instance { get; } = new();
	}

	public record Configure(CrontabSchedule Schedule, Action<Response<Unit>> Callback) : Command<Unit>(Callback);

	public record GetStatus(Action<Response<AutoScavengeStatusResponse>> Callback) : Command<AutoScavengeStatusResponse>(Callback);

	public record PauseProcess(Action<Response<Unit>> Callback) : Command<Unit>(Callback);

	public record ResumeProcess(Action<Response<Unit>> Callback) : Command<Unit>(Callback);

	public record ReceiveGossip(GossipMessage Msg) : Command<Unit>(_ => { });
}
