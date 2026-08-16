// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Infrastructure.Data;
using KurrentDB.Core;
using KurrentDB.Core.Configuration.Sources;
using KurrentDB.Core.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KurrentDB.Plugins.Kontext.Tests;

public class KontextPluginTests {
	[Test]
	public async Task IsEnabled_Returns_True_By_Default() {
		var config = new ConfigurationBuilder()
			.AddInMemoryCollection()
			.Build();

		var plugin = new KontextPlugin();
		var (enabled, _) = plugin.IsEnabled(config);

		await Assert.That(enabled).IsTrue();
	}

	[Test]
	public async Task IsEnabled_Returns_False_When_Disabled() {
		var config = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?> {
				[$"{KurrentConfigurationKeys.Prefix}:Kontext:Enabled"] = "false"
			})
			.Build();

		var plugin = new KontextPlugin();
		var (enabled, _) = plugin.IsEnabled(config);

		await Assert.That(enabled).IsFalse();
	}

	[Test]
	public async Task IsEnabled_Instructions_Point_At_Documentation() {
		var config = new ConfigurationBuilder().AddInMemoryCollection().Build();

		var plugin = new KontextPlugin();
		var (_, instructions) = plugin.IsEnabled(config);

		await Assert.That(instructions).Contains("documentation");
	}

	// -------- Storage path resolution --------

	[ClassDataSource<NodeShim>(Shared = SharedType.PerTestSession)]
	public required NodeShim NodeShim { get; init; }

	[Test]
	public async Task ConfigureServices_Resolves_Storage_Under_The_Index_Kontext_Dir() {
		var config = new ConfigurationBuilder().AddInMemoryCollection().Build();

		var services = new ServiceCollection();
		services.AddSingleton(NodeShim.Node.Services.GetRequiredService<ClusterVNodeOptions>());
		new KontextPlugin().ConfigureServices(services, config);

		var options = NodeShim.Node.Services.GetRequiredService<ClusterVNodeOptions>();
		var expectedIndex = options.Database.Index
			?? Path.Combine(options.Database.Db, ESConsts.DefaultIndexDirectoryName);
		var expectedStorage = Path.Combine(expectedIndex, "kontext");

		// The resolved path is observable through the ctor's one filesystem effect: it creates
		// the lance namespace directory before any engine I/O.
		await using var sp = services.BuildServiceProvider();
		_ = sp.GetRequiredService<KontextDataSource>();

		await Assert.That(Directory.Exists(expectedStorage)).IsTrue();
	}
}