// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using ILogger = Serilog.ILogger;

namespace EventStore.Common.Utils;

public enum ExitCode {
	Success = 0,
	Error = 1
}

public class Application {
	private static readonly ILogger Log = Serilog.Log.ForContext<Application>();

	private static Action<int> _exit = delegate {
	};

	private static int _exited;

	public static void RegisterExitAction(Action<int> exitAction) {
		Ensure.NotNull(exitAction, "exitAction");

		_exit = exitAction;
	}

	public static void ExitSilent(int exitCode, string reason) => Exit(exitCode, reason, true);
	public static void Exit(ExitCode exitCode, string reason) => Exit((int)exitCode, reason);
	public static void Exit(int exitCode, string reason) => Exit(exitCode, reason, false);

	private static void Exit(int exitCode, string reason, bool silent) {
		if (Interlocked.CompareExchange(ref _exited, 1, 0) != 0)
			return;

		Ensure.NotNullOrEmpty(reason, "reason");

		if (!silent) {
			if (exitCode != 0)
				Log.Error("Exiting with exit code: {exitCode}.\nExit reason: {e}", exitCode, reason);
			else
				Log.Information("Exiting with exit code: {exitCode}.\nExit reason: {e}", exitCode, reason);
		}

		_exit?.Invoke(exitCode);
	}
}
