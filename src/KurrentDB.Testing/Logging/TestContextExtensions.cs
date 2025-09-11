using Serilog.Extensions.Logging;
using ILogger = Serilog.ILogger;

namespace KurrentDB.Testing.Logging;

public static class TestContextExtensions {
	const string LoggerKey   = "$TestLogger";
	const string ProviderKey = "$TestLoggerProvider";

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
