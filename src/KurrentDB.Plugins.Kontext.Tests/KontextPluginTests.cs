// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Kontext;
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

	// -------- Storage path resolution --------

	[ClassDataSource<NodeShim>(Shared = SharedType.PerTestSession)]
	public required NodeShim NodeShim { get; init; }

	[Test]
	public async Task ConfigureServices_Uses_Kontext_Path_Override_When_Set() {
		var overridePath = Path.Combine(Path.GetTempPath(), $"kontext-path-{Guid.NewGuid():N}");
		var config = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?> {
				[$"{KurrentConfigurationKeys.Prefix}:Kontext:Path"] = overridePath,
			})
			.Build();

		var services = new ServiceCollection();
		new KontextPlugin().ConfigureServices(services, config);

		await using var sp = services.BuildServiceProvider();
		var storage = sp.GetRequiredService<KontextStorageConfig>();
		await Assert.That(storage.DataPath).IsEqualTo(overridePath);
	}

	[Test]
	public async Task ConfigureServices_Falls_Back_To_Index_Kontext_Dir_When_Path_Unset() {
		var config = new ConfigurationBuilder().AddInMemoryCollection().Build();

		var services = new ServiceCollection();
		services.AddSingleton(NodeShim.Node.Services.GetRequiredService<ClusterVNodeOptions>());
		new KontextPlugin().ConfigureServices(services, config);

		await using var sp = services.BuildServiceProvider();
		var storage = sp.GetRequiredService<KontextStorageConfig>();

		var options = NodeShim.Node.Services.GetRequiredService<ClusterVNodeOptions>();
		var expectedIndex = options.Database.Index
			?? Path.Combine(options.Database.Db, ESConsts.DefaultIndexDirectoryName);
		await Assert.That(storage.DataPath).IsEqualTo(Path.Combine(expectedIndex, "kontext"));
	}
}