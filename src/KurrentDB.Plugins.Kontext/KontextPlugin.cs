using EventStore.Plugins;
using Kurrent.Kontext;
using KurrentDB.Core;
using KurrentDB.Core.Configuration.Sources;
using KurrentDB.Core.Settings;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModelContextProtocol.AspNetCore;

namespace KurrentDB.Plugins.Kontext;

public class KontextPlugin() : SubsystemsPlugin(name: "Kontext") {
	const string McpPath = "/mcp/kontext";
	const string KontextDirName = "kontext";

	public override (bool Enabled, string EnableInstructions) IsEnabled(IConfiguration configuration) {
		var enabled = configuration.GetValue($"{KurrentConfigurationKeys.Prefix}:Kontext:Enabled", false);
		return (enabled, "Set KurrentDB__Kontext__Enabled to true to enable the Kontext agent memory plugin.");
	}

	public override void ConfigureServices(IServiceCollection services, IConfiguration configuration) {
		var section = configuration.GetSection($"{KurrentConfigurationKeys.Prefix}:Kontext");

		var kontextConfig = new KontextConfig {
			DisableMemory = section.GetValue<bool>("DisableMemory"),
			DisableRAG = section.GetValue<bool>("DisableRAG"),
			DisableImports = section.GetValue<bool>("DisableImports"),
			ReadOnly = section.GetValue<bool>("ReadOnly"),
		};

		services.TryAddSingleton(kontextConfig);
		services.TryAddSingleton(sp => {
			var kontextDir = ResolveKontextDirectory(sp);
			return new KontextSearchConfig { DataPath = kontextDir };
		});
		services.TryAddSingleton(sp => {
			var kontextDir = ResolveKontextDirectory(sp);
			return new KontextCheckpointConfig { CheckpointFile = Path.Combine(kontextDir, ".checkpoint") };
		});
		services.TryAddSingleton(new KontextAgentMemoryConfig { Stream = "$kontext-memory" });

		services.AddHttpContextAccessor();
		services.AddSingleton<IKontextStreamAccessChecker, KontextStreamAccessChecker>();
		services.AddSingleton<IKontextClient, KontextClient>();

		services.AddKontext(new ConfigurationBuilder().Build());
		services.AddKontextMcp().WithHttpTransport();
	}

	public override void ConfigureApplication(IApplicationBuilder app, IConfiguration configuration) {
		app.UseEndpoints(endpoints => {
			endpoints.MapMcp(McpPath);
		});
	}

	static string ResolveKontextDirectory(IServiceProvider sp) {
		var options = sp.GetRequiredService<ClusterVNodeOptions>();
		var indexPath = options.Database.Index
			?? Path.Combine(options.Database.Db, ESConsts.DefaultIndexDirectoryName);

		return Path.Combine(indexPath, KontextDirName);
	}
}
