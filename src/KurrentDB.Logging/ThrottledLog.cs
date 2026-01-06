// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Serilog;

// ReSharper disable once CheckNamespace

namespace KurrentDB.Common.Log;

public class ThrottledLog<T>(TimeSpan duration) {
	private readonly ILogger _log = Serilog.Log.ForContext<T>();
	private readonly long _duration = duration.Ticks;
	private long _lastLogged = DateTime.UnixEpoch.Ticks;

	public bool Warning(string message) {
		bool canLog = CanLog();
		if (canLog)
			_log.Warning(message);
		return canLog;
	}

	public bool Fatal(string message) {
		bool canLog = CanLog();
		if (canLog)
			_log.Fatal(message);
		return canLog;
	}

	public bool Information(string message) {
		bool canLog = CanLog();
		if (canLog)
			_log.Information(message);
		return canLog;
	}

	public bool Error(string message) {
		bool canLog = CanLog();
		if (canLog)
			_log.Error(message);
		return canLog;
	}

	private bool CanLog() {
		var currentTime = DateTime.Now.Ticks;
		bool canLog = false;

		// double-checked locking
		if (currentTime - _lastLogged >= _duration) {
			lock (_log) {
				if (currentTime - _lastLogged >= _duration) {
					_lastLogged = currentTime;
					canLog = true;
				}
			}
		}

		// perform actual logging outside synchronization so that subsequent calls to this method which are not going to log can be returned quickly
		// logging outside synchronization is safe since Serilog itself is thread-safe
		return canLog;
	}
}
