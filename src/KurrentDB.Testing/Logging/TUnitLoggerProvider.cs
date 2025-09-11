// // Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// // Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).
//
// using Microsoft.Extensions.Logging;
// using Serilog.Core;
// using Serilog.Events;
// using Serilog.Extensions.Logging;
//
// namespace KurrentDB.Testing.Logging;
//
// /// <summary>
// /// An <see cref="ILoggerProvider"/> that uses the current <see cref="TestContext"/>'s
// /// logger provider if available, otherwise falls back to a default
// /// <see cref="SerilogLoggerProvider"/> that uses the <see cref="Serilog.Log.Logger"/>
// /// static logger.
// /// </summary>
// public sealed class TUnitLoggerProvider : ILoggerProvider, ILogEventEnricher, ISupportExternalScope {
// 	static readonly SerilogLoggerProvider DefaultProvider = new();
//
// 	static SerilogLoggerProvider ContextProvider =>
// 		TestContext.Current is not null ? TestContext.Current.GetTestLoggerProvider() : DefaultProvider;
//
// 	public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory) =>
// 		ContextProvider.Enrich(logEvent, propertyFactory);
//
// 	public ILogger CreateLogger(string categoryName) =>
// 		ContextProvider.CreateLogger(categoryName);
//
// 	public void SetScopeProvider(IExternalScopeProvider scopeProvider) =>
// 		ContextProvider.SetScopeProvider(scopeProvider);
//
// 	public void Dispose() { }
// }
