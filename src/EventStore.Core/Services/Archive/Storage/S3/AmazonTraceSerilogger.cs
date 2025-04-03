// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Diagnostics;
using Amazon.Runtime.Internal.Util;
using Serilog.Events;

namespace EventStore.Core.Services.Archive.Storage.S3;

public class AmazonTraceSerilogger(Func<LogEventLevel, Exception, LogEventLevel> adjustLevel) : TraceListener {
	private static readonly Serilog.ILogger Logger = Serilog.Log.ForContext(Serilog.Core.Constants.SourceContextPropertyName, "Amazon");

	public override void TraceData(TraceEventCache eventCache, string source, TraceEventType eventType, int id,
		object data) {
		Log(eventType, data);
	}

	public override void TraceData(TraceEventCache eventCache, string source, TraceEventType eventType, int id,
		params object[] data) {
		Log(eventType, data);
	}

	public override void TraceEvent(TraceEventCache eventCache, string source, TraceEventType eventType, int id) {
	}

	public override void TraceEvent(TraceEventCache eventCache, string source, TraceEventType eventType, int id,
		string format, params object[] args) {
		Log(eventType, string.Format(format, args));
	}

	public override void TraceEvent(TraceEventCache eventCache, string source, TraceEventType eventType, int id,
		string message) {
		Log(eventType, message);
	}

	public override void TraceTransfer(TraceEventCache eventCache, string source, int id, string message,
		Guid relatedActivityId) {
		Log(TraceEventType.Transfer, relatedActivityId);
	}

	public override void Write(string message) {
		if (message is not null)
			Log(TraceEventType.Information, message);
	}

	public override void WriteLine(string message) {
		if (message is not null)
			Log(TraceEventType.Information, message);
	}

	private void Log(TraceEventType eventType, params object[] data) {
		if (data.Length is 0)
			return;

		var exception = data.Length > 1 ? data[1] as Exception : null;

		if (data[0] is not LogMessage logMessage)
			return;

		var logLevel = adjustLevel(GetLogLevel(eventType), exception);
		Logger.Write(logLevel, exception, logMessage.Format, logMessage.Args);
	}

	private static LogEventLevel GetLogLevel(TraceEventType eventType) {
		return eventType switch {
			TraceEventType.Verbose => LogEventLevel.Verbose,
			TraceEventType.Information => LogEventLevel.Information,
			TraceEventType.Warning => LogEventLevel.Warning,
			TraceEventType.Error => LogEventLevel.Error,
			TraceEventType.Critical => LogEventLevel.Fatal,
			_ => LogEventLevel.Verbose
		};
	}
}
