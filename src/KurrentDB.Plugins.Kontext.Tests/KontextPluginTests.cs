using Kurrent.Kontext;
using KurrentDB.Core;
using KurrentDB.Core.Configuration.Sources;
using KurrentDB.Core.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KurrentDB.Plugins.Kontext.Tests;

public class KontextPluginTests {
	[Test]
	public async Task IsEnabled_Returns_False_By_Default() {
		var config = new ConfigurationBuilder()
			.AddInMemoryCollection()
			.Build();

		var plugin = new KontextPlugin();
		var (enabled, _) = plugin.IsEnabled(config);

		await Assert.That(enabled).IsFalse();
	}

	[Test]
	public async Task IsEnabled_Returns_True_When_Configured() {
		var config = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?> {
				[$"{KurrentConfigurationKeys.Prefix}:Kontext:Enabled"] = "true"
			})
			.Build();

		var plugin = new KontextPlugin();
		var (enabled, _) = plugin.IsEnabled(config);

		await Assert.That(enabled).IsTrue();
	}

	[Test]
	public async Task IsEnabled_Instructions_Mention_Config_Key() {
		var config = new ConfigurationBuilder().AddInMemoryCollection().Build();

		var plugin = new KontextPlugin();
		var (_, instructions) = plugin.IsEnabled(config);

		await Assert.That(instructions).Contains("KurrentDB__Kontext__Enabled");
	}

	// -------- Fixed config defaults --------

	[ClassDataSource<NodeShim>(Shared = SharedType.PerTestSession)]
	public required NodeShim NodeShim { get; init; }

	[Test]
	public void Defaults_Place_Files_Under_Index_Kontext_Dir() {
		var options = NodeShim.Node.Services.GetRequiredService<ClusterVNodeOptions>();
		var expectedIndex = options.Database.Index
			?? Path.Combine(options.Database.Db, ESConsts.DefaultIndexDirectoryName);
		var expectedDir = Path.Combine(expectedIndex, "kontext");

		var searchConfig = new KontextSearchConfig { DataPath = expectedDir };
		var checkpointConfig = new KontextCheckpointConfig { CheckpointFile = Path.Combine(expectedDir, ".checkpoint") };

		searchConfig.DataPath.ShouldStartWith(expectedIndex);
		checkpointConfig.CheckpointFile.ShouldStartWith(expectedIndex);
		checkpointConfig.CheckpointFile.ShouldEndWith(".checkpoint");
	}

	[Test]
	public void AgentMemory_Uses_Fixed_Stream_Name() {
		var config = new KontextAgentMemoryConfig { Stream = "$kontext-memory" };
		config.Stream.ShouldBe("$kontext-memory");
	}
}
