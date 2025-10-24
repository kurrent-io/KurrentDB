// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Serilog;
using Serilog.Formatting.Display;
using Serilog.Sinks.InMemory;

namespace KurrentDB.Core.Tests.Helpers;


public static class MiniNodeLogging {
	// don't use InMemorySink.Instance, it is async local.
	private static readonly InMemorySink InMemorySink = new();

	private static readonly ILogger Logger = new LoggerConfiguration()
		.Enrich.WithProcessId()
		.Enrich.WithThreadId()
		.Enrich.FromLogContext()
		.MinimumLevel.Debug()
		.WriteTo.Sink(InMemorySink)
		.CreateLogger();

	private const string Template =
		"MiniNode: [{ProcessId,5},{ThreadId,2},{Timestamp:HH:mm:ss.fff},{Level:u3}] {Message}{NewLine}{Exception}";

	public static void Setup() {
		Log.Logger = Logger;
	}

	public static void WriteLogs() {
		TestContext.Error.WriteLine("MiniNode: Start writing logs...");

		var sb = new StringBuilder(256);
		var f = new MessageTemplateTextFormatter(Template);
		using var tw = new StringWriter(sb);
		using var snapshot = InMemorySink.Snapshot();
		InMemorySink.Dispose(); // clear the accumulated logs


		foreach (var e in snapshot.LogEvents.ToList()) {
			sb.Clear();
			f.Format(e, tw);
			TestContext.Error.Write(sb.ToString());
		}

		TestContext.Error.WriteLine("MiniNode: Writing logs done.");
	}

	public static void Clear() {
		Log.Logger = Serilog.Core.Logger.None;
		InMemorySink.Dispose();
	}
}
