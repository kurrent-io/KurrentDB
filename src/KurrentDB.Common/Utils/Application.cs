// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using ILogger = Serilog.ILogger;

namespace KurrentDB.Common.Utils;

public enum ExitCode {
	Success = 0,
	Error = 1
}

public class Application {
	private static readonly ILogger Log = Serilog.Log.ForContext<Application>();

	private static readonly Action<int, string> NoExitAction = delegate {
	};

	private static Action<int, string> _exit = NoExitAction;

	private static int _exited;

	/// <summary>
	/// Sets what happens when the node asks to exit, replacing whatever was set before.
	/// </summary>
	/// <returns>
	/// Disposing it takes the action away again and lets the next one registered be called, so that a host
	/// running one node after another in the same process hears from each of them. It does nothing once a
	/// different action has replaced this one.
	/// </returns>
	public static IDisposable RegisterExitAction(Action<int, string> exitAction) {
		Ensure.NotNull(exitAction, "exitAction");

		_exit = exitAction;
		return new ExitActionRegistration(exitAction);
	}

	private sealed class ExitActionRegistration(Action<int, string> exitAction) : IDisposable {
		public void Dispose() {
			if (ReferenceEquals(Interlocked.CompareExchange(ref _exit, NoExitAction, exitAction), exitAction))
				Interlocked.Exchange(ref _exited, 0);
		}
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

		_exit?.Invoke(exitCode, reason);
	}
}
