using Serilog.Extensions.Logging;

namespace KurrentDB.Testing.Logging;

public static class TestContextExtensions {
	const string ProviderKey = "$TestLoggerProvider";

	public static void SetLoggerProvider(this TestContext ctx, SerilogLoggerProvider loggerProvider) {
		ctx.ObjectBag.Add(ProviderKey, loggerProvider);
	}

	public static SerilogLoggerProvider GetLoggerProvider(this TestContext? ctx) =>
		ctx is not null && ctx.ObjectBag.TryGetValue(ProviderKey, out var val) && val is SerilogLoggerProvider provider
			? provider : throw new InvalidOperationException("Failed to find LoggerProvider in the TestContext ObjectBag!");
}
