// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Common.Utils;
using ILogger = Serilog.ILogger;

namespace EventStore.Core.Tests.Helpers;

public class ClientApiLoggerBridge : EventStore.ClientAPI.ILogger {
	public static readonly ClientApiLoggerBridge Default =
		new ClientApiLoggerBridge(Serilog.Log.ForContext(Serilog.Core.Constants.SourceContextPropertyName,
			"client-api"));

	private readonly Serilog.ILogger _log;

	public ClientApiLoggerBridge(ILogger log) {
		Ensure.NotNull(log, "log");
		_log = log;
	}

	public void Error(string format, params object[] args) {
		if (args.Length == 0)
			_log.Error(format);
		else
			_log.Error(format, args);
	}

	public void Error(Exception ex, string format, params object[] args) {
		if (args.Length == 0)
			_log.Error(ex, format);
		else
			_log.Error(ex, format, args);
	}

	public void Info(string format, params object[] args) {
		if (args.Length == 0)
			_log.Information(format);
		else
			_log.Information(format, args);
	}

	public void Info(Exception ex, string format, params object[] args) {
		if (args.Length == 0)
			_log.Information(ex, format);
		else
			_log.Information(ex, format, args);
	}

	public void Debug(string format, params object[] args) {
		if (args.Length == 0)
			_log.Debug(format);
		else
			_log.Debug(format, args);
	}

	public void Debug(Exception ex, string format, params object[] args) {
		if (args.Length == 0)
			_log.Debug(ex, format);
		else
			_log.Debug(ex, format, args);
	}
}
