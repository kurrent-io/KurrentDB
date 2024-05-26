using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using static System.Console;
using static System.Environment;
using static System.StringComparison;
using RuntimeInformation = System.Runtime.RuntimeInformation;

namespace EventStore.Testing;

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

		// var totalPhysicalMemory  = SystemRuntimeStats.GetTotalPhysicalMemory().Bytes();
		// var totalAvailableMemory = SystemRuntimeStats.GetTotalAvailableMemory().Bytes();
		// var totalFreeMemory      = SystemRuntimeStats.GetTotalFreeMemory().Bytes();
		//
		// WriteLine($"APP: Processor Count        : {ProcessorCount}");
		// WriteLine($"APP: Total Physical Memory  : {totalPhysicalMemory.ToFullWords()}");
		// WriteLine($"APP: Total Available Memory : {totalAvailableMemory.ToFullWords()}");
		// WriteLine($"APP: Total Free Memory      : {totalFreeMemory.ToFullWords()}");
		// WriteLine($"APP: Thread Pool            : {workerThreads} Worker | {completionPortThreads} Async");

		ForegroundColor = ConsoleColor.Magenta;
	}

	public static IConfiguration Configuration      { get; }
	public static bool           IsProduction       { get; }
	public static bool           IsDevelopment      { get; }
	public static bool           IsStaging          { get; }
	public static string         Environment        { get; }
	public static bool           DebuggerIsAttached { get; }

	public static bool IsEnvironment(string environmentName) => Environment.Equals(environmentName, InvariantCultureIgnoreCase);
}