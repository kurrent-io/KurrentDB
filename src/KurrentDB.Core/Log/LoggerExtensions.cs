// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Serilog;
using Serilog.Core;

// ReSharper disable once CheckNamespace
namespace KurrentDB.Common.Log;

public static class LoggerExtensions {
	public static ILogger ForContext(this ILogger logger, string context)
		=> logger.ForContext(Constants.SourceContextPropertyName, context);
}
