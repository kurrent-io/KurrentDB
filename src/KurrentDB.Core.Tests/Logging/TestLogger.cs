// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Serilog;
using Serilog.Core;

namespace KurrentDB.Core.Tests.Logging;

public static class TestLogger {
	public static readonly Logger Logger = new LoggerConfiguration().WriteTo.TestOutput().CreateLogger();

	public static ILogger Create<T>() => Logger.ForContext<T>();
}
