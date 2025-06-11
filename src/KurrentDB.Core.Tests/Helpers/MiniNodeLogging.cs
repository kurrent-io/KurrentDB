// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Serilog;
using Serilog.Core;
using Serilog.Formatting.Display;
using Serilog.Sinks.InMemory;

namespace KurrentDB.Core.Tests.Helpers;


public static class MiniNodeLogging {
	private static InMemorySink _sink;
	private static ILogger CreateLogger => new LoggerConfiguration()
		.Enrich.WithProcessId()
		.Enrich.WithThreadId()
		.Enrich.FromLogContext()
		.MinimumLevel.Error()
		.WriteTo.InMemory()
		.CreateLogger();

	private const string Template =
		"MiniNode: [{ProcessId,5},{ThreadId,2},{Timestamp:HH:mm:ss.fff},{Level:u3}] {Message}{NewLine}{Exception}";

	public static void Setup() {
		Log.Logger = CreateLogger;
		_sink = InMemorySink.Instance; //qq instance is specific to asynccontext or something like that
	}

	public static void WriteLogs() {
		TestContext.Error.WriteLine("MiniNode: Start writing logs...");

		var sb = new StringBuilder(256);
		var f = new MessageTemplateTextFormatter(Template);
		using var tw = new StringWriter(sb);
		using var snapshot = _sink.Snapshot();
		foreach (var e in snapshot.LogEvents.ToList()) {
			sb.Clear();
			f.Format(e, tw);
			TestContext.Error.Write(sb.ToString());
		}

		TestContext.Error.WriteLine("MiniNode: Writing logs done.");
	}

	public static void Clear() {
		Log.Logger = Logger.None;
		_sink.Dispose();
	}
}
