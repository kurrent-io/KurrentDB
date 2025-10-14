// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Serilog.Extensions.Logging;

namespace KurrentDB.Testing.TUnit;

[PublicAPI]
public static partial class TestContextExtensions {
	/// <summary>
	/// Attempts to extract an item from the TestContext's ObjectBag.
	/// </summary>
	public static bool TryExtractItem(this TestContext? ctx, string key, [MaybeNullWhen(false)] out object item) {
		if (ctx is not null && ctx.ObjectBag.TryGetValue(key, out var val)) {
			item = val!;
			return true;
		}

		item = null;
		return false;
	}

	/// <summary>
	/// Attempts to extract an item of type T from the TestContext's ObjectBag.
	/// </summary>
	public static bool TryExtractItem<T>(this TestContext? ctx, string? key, out T item) {
		key ??= $"${typeof(T).FullName ?? typeof(T).Name}";

		if (ctx.TryExtractItem(key, out var i)) {
			item = (T)i;
			return true;
		}

		item = default!;
		return false;
	}

	/// <summary>
	/// Attempts to extract an item of type T from the TestContext's ObjectBag.
	/// </summary>
	public static bool TryExtractItem<T>(this TestContext? ctx, out T item) where T : notnull =>
		TryExtractItem(ctx, null, out item);

	/// <summary>
	/// Extracts an item from the TestContext's ObjectBag, throwing if the key does not exist.
	/// </summary>
	public static T ExtractItem<T>(this TestContext? ctx, string? key = null) where T : notnull {
		key ??= $"${typeof(T).FullName ?? typeof(T).Name}";
		return !TryExtractItem(ctx, key, out T item)
			? throw new InvalidOperationException($"'{key}' item not found in the TestContext ObjectBag!")
			: item;
	}

	/// <summary>
	/// Attempts to inject an item into the TestContext's ObjectBag.
	/// </summary>
	public static bool TryInjectItem<T>(this TestContext? ctx, T item, string? key = null) where T : notnull {
		key ??= $"${typeof(T).FullName ?? typeof(T).Name}";
		return ctx?.ObjectBag.TryAdd(key, item) ?? throw new InvalidOperationException("No current TestContext available!");
	}

	/// <summary>
	/// Injects an item into the TestContext's ObjectBag, throwing if the key already exists.
	/// </summary>
	public static T InjectItem<T>(this TestContext? ctx, T item, string? key = null) where T : notnull {
		key ??= $"${typeof(T).FullName ?? typeof(T).Name}";
		return !TryInjectItem(ctx, item, key)
			? throw new InvalidOperationException($"'{key}' item already exists in the TestContext ObjectBag!")
			: item;
	}
}

public static partial class TestContextLoggingExtensions {
	const string LoggerFactoryKey = "$TestLoggerFactory";
    const string LoggerKey        = "$TestLogger";

    public static Disposable AddLogging(this TestContext ctx, Serilog.ILogger scopedTestLogger) {
        ILoggerFactory factory = new SerilogLoggerFactory(scopedTestLogger, true);
        ctx.InjectItem(factory, LoggerFactoryKey);
        ctx.InjectItem(factory.CreateLogger(null!), LoggerKey);

        return Disposable.Create.With(ctx.RemoveLogging);
    }

    public static void RemoveLogging(this TestContext ctx) {
        ctx.ObjectBag.Remove(LoggerFactoryKey);
        ctx.ObjectBag.Remove(LoggerKey);
    }

    public static bool TryGetLoggerFactory(this TestContext? ctx, out ILoggerFactory loggerFactory) =>
        ctx.TryExtractItem(LoggerFactoryKey, out loggerFactory);

    public static ILoggerFactory LoggerFactory(this TestContext? ctx) =>
        ctx.ExtractItem<ILoggerFactory>(LoggerFactoryKey);

    public static ILogger Logger(this TestContext? ctx) =>
        ctx.ExtractItem<ILogger>(LoggerKey);

    public static ILogger<T> CreateLogger<T>(this TestContext? ctx) =>
        ctx.LoggerFactory().CreateLogger<T>();

    public static ILogger CreateLogger(this TestContext? ctx, string categoryName) =>
        ctx.LoggerFactory().CreateLogger(categoryName);
}
