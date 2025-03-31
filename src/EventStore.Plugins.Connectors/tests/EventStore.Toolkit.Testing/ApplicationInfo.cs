using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using static System.Console;
using static System.Environment;
using static System.StringComparison;

namespace EventStore.Toolkit.Testing;

/// <summary>
/// Loads configuration and provides information about the application environment.
/// </summary>
[PublicAPI]
public static class Application {
	static Application() {
		ForegroundColor = ConsoleColor.Magenta;

		WriteLine($"APP: {AppContext.BaseDirectory}");

		Environment = GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? Environments.Development;

		var builder = new ConfigurationBuilder()
			.AddJsonFile("appsettings.json", true)
			.AddJsonFile($"appsettings.{Environment}.json", true)                    // Accept default naming convention
			.AddJsonFile($"appsettings.{Environment.ToLowerInvariant()}.json", true) // Linux is case sensitive
			.AddJsonFile($"otelsettings.{Environment}.json", true)
			.AddJsonFile($"otel-tracing-settings.{Environment}.json", true)
			.AddEnvironmentVariables();

		Configuration = builder.Build();

		WriteLine($"APP: {Environment} configuration loaded "
		        + $"with {Configuration.AsEnumerable().Count()} entries "
		        + $"from {builder.Sources.Count} sources.");

		IsDevelopment = IsEnvironment(Environments.Development);
		IsStaging     = IsEnvironment(Environments.Staging);
		IsProduction  = IsEnvironment(Environments.Production);

		DebuggerIsAttached = Debugger.IsAttached;

		ThreadPool.GetAvailableThreads(out var workerThreads, out var completionPortThreads);

		ForegroundColor = ConsoleColor.Blue;

		WriteLine($"APP: Processor Count  : {ProcessorCount}");
		WriteLine($"APP: ThreadPool       : {workerThreads} Worker | {completionPortThreads} Async");

		ForegroundColor = ConsoleColor.Magenta;
	}

	public static IConfiguration Configuration      { get; }
	public static bool           IsProduction       { get; }
	public static bool           IsDevelopment      { get; }
	public static bool           IsStaging          { get; }
	public static string         Environment        { get; }
	public static bool           DebuggerIsAttached { get; }

	public static bool IsEnvironment(string environmentName) => Environment.Equals(environmentName, InvariantCultureIgnoreCase);

	public static class OperatingSystem {
		public static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
		public static bool IsMacOS   => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
		public static bool IsLinux   => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
	}
}