// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Testing.TUnit;
using Microsoft.Extensions.Logging;
using Serilog.Extensions.Logging;

namespace KurrentDB.Testing;

/// <summary>
/// An <see cref="ILoggerProvider"/> that uses the current <see cref="TestContext"/>'s
/// logger provider if available, otherwise falls back to a default
/// <see cref="ILoggerProvider"/>.
/// <remarks>
/// This allows scenarios where tests can have their own isolated logger providers
/// (e.g. to capture logs per-test, or to configure different sinks per-test),
/// while still allowing code that is not test-aware to log using the global logger.
/// </remarks>
/// </summary>
public abstract record ToolkitTestLoggerProvider<T>(T GlobalProvider)
	: ILoggerProvider, ISupportExternalScope where T : ILoggerProvider, ISupportExternalScope {

	T Proxy => TestContext.Current.TryExtractItem<T>(out var provider)
		? provider : GlobalProvider;

	public ILogger CreateLogger(string categoryName) =>
		Proxy.CreateLogger(categoryName);

	public void SetScopeProvider(IExternalScopeProvider scopeProvider) =>
		Proxy.SetScopeProvider(scopeProvider);

	public void Dispose() {
		// No-op; the TestExecutor is responsible for disposing of any logger providers
	}
}

public abstract record TestLoggerProvider : ILoggerProvider {
    static readonly ILoggerProvider DefaultProvider = new SerilogLoggerProvider(dispose: false);

    public ILogger CreateLogger(string categoryName) =>
        TestContext.Current.TryGetLoggerFactory(out var loggerFactory)
            ? loggerFactory.CreateLogger(categoryName)
            : DefaultProvider.CreateLogger(categoryName);

    public void Dispose() {
        // No-op; the TestExecutor is responsible for disposing of any logger providers
    }
}

/// <summary>
/// The logger provider that integrates Serilog with the Microsoft.Extensions.Logging framework.
/// It dynamically returns loggers with context about the current test being executed or
/// the default logger if no test context is available.
/// </summary>
/// <remarks>
/// This allows scenarios where tests can have their own isolated logger providers
/// (e.g. to capture logs per-test, or to configure different sinks per-test),
/// while still allowing code that is not test-aware to log using the global logger.
/// </remarks>
public sealed record ToolkitTestLoggerProvider() : ToolkitTestLoggerProvider<SerilogLoggerProvider>(new SerilogLoggerProvider(dispose: false)) {
    public static readonly ToolkitTestLoggerProvider Instance = new();
}

//
// public abstract record ToolkitTestLoggerProvider<T>(T GlobalProvider)
//     : ILoggerProvider, ISupportExternalScope where T : ILoggerProvider, ISupportExternalScope {
//
//     T Proxy => TestContext.Current.TryExtractItem<T>(out var provider)
//         ? provider : GlobalProvider;
//
//     public ILogger CreateLogger(string categoryName) =>
//         Proxy.CreateLogger(categoryName);
//
//     public void SetScopeProvider(IExternalScopeProvider scopeProvider) =>
//         Proxy.SetScopeProvider(scopeProvider);
//
//     public void Dispose() {
//         // No-op; the TestExecutor is responsible for disposing of any logger providers
//     }
// }

public class ToolkitTestLoggerFactory : ILoggerFactory {
    readonly ILoggerFactory _defaultLoggerFactory = new LoggerFactory();

    public ILogger CreateLogger(string categoryName) {
        return TestContext.Current.TryGetLoggerFactory(out var factory)
            ? factory.CreateLogger(categoryName)
            : _defaultLoggerFactory.CreateLogger(categoryName);
    }

    public void AddProvider(ILoggerProvider provider) =>
        throw new NotImplementedException();

    public void Dispose() {
        // No-op; the TestExecutor is responsible for disposing of any logger providers
    }
}

//
// [ProviderAlias("TUnit")]
// public sealed class ToolkitTestLoggerProvider : ILoggerProvider
// {
//     public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName) =>
//         TUnitLoggerWrapper.Instance;
//
//     public void Dispose()
//     {
//     }
// }
