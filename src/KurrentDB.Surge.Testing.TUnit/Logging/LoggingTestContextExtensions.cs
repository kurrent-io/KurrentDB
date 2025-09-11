// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace Kurrent.Surge.Testing.TUnit.Logging;

public static class LoggingTestContextExtensions {
	const string LoggerFactoryKey = "$ToolkitLoggerFactory";

	public static void SetLoggerFactory(this TestContext context, ILoggerFactory loggerFactory) {
		Ensure.NotNull(loggerFactory);
		context.ObjectBag[LoggerFactoryKey] = loggerFactory;
	}

	public static bool TryGetLoggerFactory(this TestContext? context, [MaybeNullWhen(false)] out ILoggerFactory loggerFactory) {
		if (context is not null
		 && context.ObjectBag.TryGetValue(LoggerFactoryKey, out var value)
		 && value is ILoggerFactory factory) {
			loggerFactory = factory;
			return true;
		}

		loggerFactory = null!;
		return false;
	}

	public static ILoggerFactory LoggerFactory(this TestContext? context)
		=> context is not null
		&& context.ObjectBag.TryGetValue(LoggerFactoryKey, out var value)
		&& value is ILoggerFactory loggerFactory
			? loggerFactory
			: throw new InvalidOperationException("Testing toolkit logger factory not found!");
}
