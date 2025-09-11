// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics;
using Serilog;
using Serilog.Extensions.Logging;

namespace KurrentDB.Testing;

public static class TestContextExtensions {
	const string TestUidKey  = "$TestUid";
	const string LoggerKey   = "$TestLogger";
	const string ProviderKey = "$TestLoggerProvider";

	public static Guid AssignTestUid(this TestContext ctx, Guid testUid) {
		Debug.Assert(testUid != Guid.Empty, "TestUid cannot be empty");
		ctx.ObjectBag[TestUidKey] = testUid;
		return testUid;
	}

	public static Guid TestUid(this TestContext? ctx, Guid? defaultTestUid = null) {
		Debug.Assert(defaultTestUid is not null & defaultTestUid != Guid.Empty, "Default TestUid cannot be null or empty");
		return ctx is null || !ctx.ObjectBag.TryGetValue(TestUidKey, out var val) || val is not Guid uid || uid == Guid.Empty
			? defaultTestUid ?? throw new KeyNotFoundException("Failed to find TestUid in the TestContext ObjectBag!")
			: uid;
	}

	public static void AssignLogger(this TestContext ctx, ILogger logger) {
		var provider = new SerilogLoggerProvider(logger);

		ctx.ObjectBag[LoggerKey]   = logger;
		ctx.ObjectBag[ProviderKey] = provider;
	}

	public static ILogger Logger(this TestContext? ctx) =>
		ctx is not null && ctx.ObjectBag.TryGetValue(LoggerKey, out var val) && val is ILogger logger
			? logger : throw new KeyNotFoundException("Failed to find Logger in the TestContext ObjectBag!");

	public static SerilogLoggerProvider LoggerProvider(this TestContext? ctx) =>
		ctx is not null && ctx.ObjectBag.TryGetValue(ProviderKey, out var val) && val is SerilogLoggerProvider provider
			? provider : throw new InvalidOperationException("Failed to find LoggerProvider in the TestContext ObjectBag!");
}
