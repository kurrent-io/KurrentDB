// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using Serilog;
using Serilog.Core;

// ReSharper disable once CheckNamespace
namespace KurrentDB.Common.Log;

public static class LoggerExtensions {
	public static ILogger ForContext(this ILogger logger, string context)
		=> logger.ForContext(Constants.SourceContextPropertyName, context);
}
