// // Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// // Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).
//
// using System.Reactive.Subjects;
// using Humanizer;
// using KurrentDB.Testing.Logging;
// using KurrentDB.Testing.OpenTelemetry;
// using Microsoft.Extensions.Configuration;
// using Serilog;
// using Serilog.Core.Enrichers;
// using Serilog.Events;
// using Serilog.Exceptions;
// using Serilog.Extensions.Logging;
// using TUnit.Core.Interfaces;
//
// using static Serilog.Core.Constants;
//
// using IConfiguration = Microsoft.Extensions.Configuration.IConfiguration;
//
// namespace KurrentDB.Testing;
//
// public class KurrentTestExecuter : ITestExecutor, IHookExecutor {
// 	static readonly PropertyEnricher DefaultSourceContextEnricher = new(SourceContextPropertyName, nameof(TUnit));
//
// 	static LoggerConfiguration DefaultLoggerConfig => new LoggerConfiguration()
// 		.Enrich.With(DefaultSourceContextEnricher)
// 		.Enrich.WithThreadId()
// 		.Enrich.WithProcessId()
// 		.Enrich.WithMachineName()
// 		.Enrich.FromLogContext()
// 		.Enrich.WithExceptionDetails()
// 		.MinimumLevel.Verbose();
//
// 	static IConfiguration    Configuration { get; set; } = null!;
// 	static Subject<LogEvent> LogEvents     { get; } = new();
//
// 	const string TestUidPropertyName = nameof(TestUid);
//
// 	public ValueTask ExecuteAfterTestDiscoveryHook(MethodMetadata hookMethodInfo, TestDiscoveryContext context, Func<ValueTask> action) {
// 		// to simplify the logging and tracing logic, we configure all tests here
// 		// this way we ensure that all tests have a unique ID and are properly configured for OTEL
// 		Parallel.ForEach(context.AllTests, testContext => {
// 			var testUid = testContext.AssignTestUid();
//
// 			testContext.ConfigureOtel(new(testContext.TestDetails.ClassType.Name) {
// 				ServiceInstanceId = testUid,
// 				ServiceNamespace  = testContext.TestDetails.ClassType.Namespace
// 			});
// 		});
//
// 		return ValueTask.CompletedTask;
// 	}
//
// 	public ValueTask ExecuteBeforeAssemblyHook(MethodMetadata hookMethodInfo, AssemblyHookContext context, Func<ValueTask> action) {
// 		// setup defaults
//
//
// 		KurrentTestingContext.Initialize();
//
// 		Configuration = KurrentTestingContext.Configuration;
//
// 		DenyConsoleLoggers(Configuration);
//
// 		Log.Logger = DefaultLoggerConfig
// 			.ReadFrom.Configuration(Configuration)
// 			.Enrich.WithProperty(TestUidPropertyName, Guid.Empty)
// 			.WriteTo.Observers(o => o.Subscribe(LogEvents.OnNext))
// 			.WriteTo.Console(
// 				theme: Serilog.Sinks.SystemConsole.Themes.AnsiConsoleTheme.Literate,
// 				outputTemplate: "[{Timestamp:mm:ss.fff} {Level:u3} {TestUid}] ({ThreadId:000}) {SourceContext} {NewLine}{Message}{NewLine}{Exception}{NewLine}",
// 				applyThemeToRedirectedOutput: true)
// 			.CreateLogger();
//
// 		AppLogging.Initialize(KurrentTestingContext.Configuration);
//
// 		Log.Verbose("#### Executing {TestCount} tests in assembly {AssemblyName}", context.TestCount, context.Assembly.GetName().Name);
//
// 		return ValueTask.CompletedTask;
//
// 		static void DenyConsoleLoggers(IConfiguration configuration) {
// 			var consoleLoggerEntries = configuration.AsEnumerable()
// 				.Where(x => x.Key.StartsWith("Serilog") && x.Key.EndsWith(":Name") && x.Value == "Console").ToList();
//
// 			if (consoleLoggerEntries.Count != 0)
// 				throw new InvalidOperationException("Console loggers are not allowed in the configuration");
// 		}
// 	}
//
// 	public ValueTask ExecuteAfterAssemblyHook(MethodMetadata hookMethodInfo, AssemblyHookContext context, Func<ValueTask> action) {
// 		Log.Verbose("#### Executed {TestCount} tests in assembly {AssemblyName}", context.TestCount, context.Assembly.GetName().Name);
// 		return Log.CloseAndFlushAsync(); // should we? Is it the end?
// 	}
//
// 	public ValueTask ExecuteBeforeClassHook(MethodMetadata hookMethodInfo, ClassHookContext context, Func<ValueTask> action) {
// 		Log.Verbose("#### Executing {TestCount} tests in class {ClassName}", context.TestCount, context.ClassType.Name);
// 		return ValueTask.CompletedTask;
// 	}
//
// 	public ValueTask ExecuteAfterClassHook(MethodMetadata hookMethodInfo, ClassHookContext context, Func<ValueTask> action) {
// 		Log.Verbose("#### Executed {TestCount} tests in class {ClassName}", context.TestCount, context.ClassType.Name);
// 		return ValueTask.CompletedTask;
// 	}
//
// 	public ValueTask ExecuteBeforeTestHook(MethodMetadata hookMethodInfo, TestContext context, Func<ValueTask> action) {
// 		Log.Verbose("#### Executing test {TestName}", context.TestDetails.TestName);
// 		return ValueTask.CompletedTask;
// 	}
//
// 	public async ValueTask ExecuteTest(TestContext context, Func<ValueTask> action) {
// 		var testUid = context.GetTestUid();
//
// 		var prop = new LogEventProperty(TestUidPropertyName, new ScalarValue(testUid));
//
// 		using var subscription = AppLogging.LogEvents
// 			.Subscribe(logEvent => logEvent.AddOrUpdateProperty(prop));
//
// 		var logger = Log.Logger
// 			.ForContext(SourceContextPropertyName, context.TestDetails.ClassType.FullName)
// 			.ForContext(TestUidPropertyName, testUid);
//
// 		await using var loggerProvider = new SerilogLoggerProvider(logger, dispose: true);
//
// 		context.SetTestLoggerProvider(loggerProvider);;
//
// 		context.TestStart = TimeProvider.System.GetUtcNow();
//
// 		try {
// 			await action();
// 		}
// 		finally {
// 			context.TestEnd = TimeProvider.System.GetUtcNow();
//
// 			context.Timings.Add(new("Execution", context.TestStart, context.TestEnd.Value));
//
// 			var elapsed = context.TestEnd.Value - context.TestStart;
//
// 			Log.Verbose(
// 				"#### Test {TestName} {Status} in {Elapsed}",
// 				context.TestDetails.TestName,
// 				context.Result,
// 				elapsed.Humanize(precision: 2));
// 		}
// 	}
//
// 	public ValueTask ExecuteAfterTestHook(MethodMetadata hookMethodInfo, TestContext context, Func<ValueTask> action) {
// 		if (context.Result?.Duration is null) {
// 			// this should never happen, but if it does, we want to know about it
// 			Log.Fatal("#### Test {TestName} has no result or the result does not have a duration?! Wait... WHAT?!", context.TestDetails.TestName);
// 			return ValueTask.CompletedTask;
// 		}
//
// 		var elapsed = context.Result.Duration.GetValueOrDefault();
//
// 		Log.Verbose(
// 			"#### Test {TestName} {TestState} in {Elapsed}",
// 			context.TestDetails.TestName,
// 			context.Result.State,
// 			elapsed.Humanize(precision: 2));
//
// 		return ValueTask.CompletedTask;
// 	}
//
// 	#region . Unused Hooks .
//
// 	public ValueTask ExecuteBeforeTestDiscoveryHook(MethodMetadata hookMethodInfo, BeforeTestDiscoveryContext context, Func<ValueTask> action) =>
// 		DefaultExecutor.Instance.ExecuteBeforeTestDiscoveryHook(hookMethodInfo, context, action);
//
//
//
// 	public ValueTask ExecuteBeforeTestSessionHook(MethodMetadata hookMethodInfo, TestSessionContext context, Func<ValueTask> action) =>
// 		DefaultExecutor.Instance.ExecuteBeforeTestSessionHook(hookMethodInfo, context, action);
//
// 	public ValueTask ExecuteAfterTestSessionHook(MethodMetadata hookMethodInfo, TestSessionContext context, Func<ValueTask> action) =>
// 		DefaultExecutor.Instance.ExecuteAfterTestSessionHook(hookMethodInfo, context, action);
//
// 	#endregion
// }
//
//
//
// // [Before(Assembly)]
// //
// // public class RagingTestExecuter : ITestExecutor, IHookExecutor {
// // 	static readonly PropertyEnricher DefaultSourceContextEnricher = new(Serilog.Core.Constants.SourceContextPropertyName, nameof(TUnit));
// //
// // 	static LoggerConfiguration DefaultLoggerConfig => new LoggerConfiguration()
// // 		.Enrich.With(DefaultSourceContextEnricher)
// // 		.Enrich.WithThreadId()
// // 		.Enrich.WithProcessId()
// // 		.Enrich.WithMachineName()
// // 		.Enrich.FromLogContext()
// // 		.Enrich.WithExceptionDetails()
// // 		.MinimumLevel.Verbose();
// //
// // 	static IConfiguration    Configuration { get; set; } = null!;
// // 	static Subject<LogEvent> LogEvents     { get; set; } = new();
// //
// // 	static readonly string TestUidPropertyName = "TestUid";
// //
// // 	public ValueTask ExecuteBeforeAssemblyHook(MethodMetadata hookMethodInfo, AssemblyHookContext context, Func<ValueTask> action) {
// // 		new OtelServiceMetadata("TestingToolkit") {
// // 			ServiceVersion   = "1.0.0",
// // 			ServiceNamespace = "KurrentDB.Testing",
// // 		}.UpdateEnvironmentVariables();
// //
// // 		ApplicationContext.Initialize();
// //
// // 		Configuration = ApplicationContext.Configuration;
// //
// // 		DenyConsoleLoggers(Configuration);
// //
// // 		Log.Logger = DefaultLoggerConfig
// // 			.ReadFrom.Configuration(Configuration)
// // 			.Enrich.WithProperty(TestUidPropertyName, Guid.Empty)
// // 			.WriteTo.Observers(o => o.Subscribe(LogEvents.OnNext))
// // 			.WriteTo.Console(
// // 				theme: Serilog.Sinks.SystemConsole.Themes.AnsiConsoleTheme.Literate,
// // 				outputTemplate: "[{Timestamp:mm:ss.fff} {Level:u3} {ShortTestUid}] ({ThreadId:000}) {SourceContext} {NewLine}{Message}{NewLine}{Exception}{NewLine}",
// // 				applyThemeToRedirectedOutput: true)
// // 			.CreateLogger();
// //
// // 		LoggingContext.Initialize(ApplicationContext.Configuration);
// //
// // 		Log.Debug("#### Starting tests in assembly {AssemblyName}", context.Assembly.GetName().Name);
// //
// // 		return ValueTask.CompletedTask;
// //
// // 		static void DenyConsoleLoggers(IConfiguration configuration) {
// // 			var consoleLoggerEntries = configuration.AsEnumerable()
// // 				.Where(x => x.Key.StartsWith("Serilog") && x.Key.EndsWith(":Name") && x.Value == "Console").ToList();
// //
// // 			if (consoleLoggerEntries.Count != 0)
// // 				throw new InvalidOperationException("Console loggers are not allowed in the configuration");
// // 		}
// // 	}
// //
// // 	public ValueTask ExecuteAfterAssemblyHook(MethodMetadata hookMethodInfo, AssemblyHookContext context, Func<ValueTask> action) {
// // 		Log.Debug("#### Completed tests in assembly {AssemblyName}", context.Assembly.GetName().Name);
// // 		return Log.CloseAndFlushAsync();
// // 	}
// //
// // 	public ValueTask ExecuteBeforeClassHook(MethodMetadata hookMethodInfo, ClassHookContext context, Func<ValueTask> action) {
// // 		Log.Information("---- Starting tests in class {ClassName} ----", context.ClassType.Name);
// // 		return DefaultExecutor.Instance.ExecuteBeforeClassHook(hookMethodInfo, context, action);
// // 	}
// //
// // 	public ValueTask ExecuteBeforeTestHook(MethodMetadata hookMethodInfo, TestContext context, Func<ValueTask> action) =>
// // 		DefaultExecutor.Instance.ExecuteBeforeTestHook(hookMethodInfo, context, action);
// //
// // 	public async ValueTask ExecuteTest(TestContext context, Func<ValueTask> action) {
// // 		var testUid = Guid.NewGuid();
// //
// // 		context.AssignLogger(Log.Logger.ForContext("TestUid", testUid));
// //
// // 		context.ConfigureOtel(
// // 			new(context.TestDetails.ClassType.Name) {
// // 				ServiceInstanceId = testUid.ToString(),
// // 				ServiceNamespace  = context.TestDetails.ClassType.Namespace
// // 			}
// // 		);
// //
// // 		var prop = new LogEventProperty("TestUid", new ScalarValue(testUid));
// //
// // 		using var subscription = LoggingContext.LogEvents.Subscribe(logEvent => logEvent.AddOrUpdateProperty(prop));
// //
// // 		Log.Verbose("#### Test {TestName} starting...", context.TestDetails.TestName);
// //
// // 		var start = TimeProvider.System.GetTimestamp();
// // 		try {
// // 			await action();
// // 		}
// // 		finally {
// // 			var elapsed = TimeProvider.System.GetElapsedTime(start);
// //
// // 			Log.Verbose(
// // 				"#### Test {TestName} completed in {Elapsed}",
// // 				context.TestDetails.TestName,
// // 				elapsed.Humanize(precision: 2));
// //
// // 			await context.GetTestLoggerProvider().DisposeAsync();
// // 		}
// // 	}
// //
// // 	public ValueTask ExecuteAfterTestHook(MethodMetadata hookMethodInfo, TestContext context, Func<ValueTask> action) =>
// // 		DefaultExecutor.Instance.ExecuteAfterTestHook(hookMethodInfo, context, action);
// //
// // 	public ValueTask ExecuteAfterClassHook(MethodMetadata hookMethodInfo, ClassHookContext context, Func<ValueTask> action) =>
// // 		DefaultExecutor.Instance.ExecuteAfterClassHook(hookMethodInfo, context, action);
// //
// // 	#region . Unused Hooks .
// //
// // 	public ValueTask ExecuteBeforeTestDiscoveryHook(MethodMetadata hookMethodInfo, BeforeTestDiscoveryContext context, Func<ValueTask> action) =>
// // 		DefaultExecutor.Instance.ExecuteBeforeTestDiscoveryHook(hookMethodInfo, context, action);
// //
// // 	public ValueTask ExecuteAfterTestDiscoveryHook(MethodMetadata hookMethodInfo, TestDiscoveryContext context, Func<ValueTask> action) =>
// // 		DefaultExecutor.Instance.ExecuteAfterTestDiscoveryHook(hookMethodInfo, context, action);
// //
// // 	public ValueTask ExecuteBeforeTestSessionHook(MethodMetadata hookMethodInfo, TestSessionContext context, Func<ValueTask> action) =>
// // 		DefaultExecutor.Instance.ExecuteBeforeTestSessionHook(hookMethodInfo, context, action);
// //
// // 	public ValueTask ExecuteAfterTestSessionHook(MethodMetadata hookMethodInfo, TestSessionContext context, Func<ValueTask> action) =>
// // 		DefaultExecutor.Instance.ExecuteAfterTestSessionHook(hookMethodInfo, context, action);
// //
// // 	#endregion
// // }
