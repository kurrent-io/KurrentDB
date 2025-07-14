// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable AccessToDisposedClosure

using EventStore.Plugins.Diagnostics;

namespace EventStore.Plugins.Tests.Diagnostics;

public class PluginDiagnosticsDataCollectorTests {
	[Fact]
	public void can_collect_diagnostics_data_from_plugin() {
		using var plugin = new TestPlugin(pluginName: Guid.NewGuid().ToString());

		using var sut = PluginDiagnosticsDataCollector.Start(plugin.DiagnosticsName);

		plugin.PublishDiagnosticsData(new() { ["enabled"] = plugin.Enabled });

		var diagnosticsData = sut.CollectedEvents(plugin.DiagnosticsName).Should().ContainSingle().Which;
		diagnosticsData.Data["enabled"].Should().Be(plugin.Enabled);
		diagnosticsData.CollectionMode.Should().Be(PluginDiagnosticsDataCollectionMode.Snapshot);
	}
	
	[Fact]
	public void can_collect_diagnostics_data_from_multiple_plugins() {
		using var pluginOne = new TestPlugin(pluginName: Guid.NewGuid().ToString());
		using var pluginTwo = new TestPlugin(pluginName: Guid.NewGuid().ToString());

		using var sut = PluginDiagnosticsDataCollector.Start(
			pluginOne.DiagnosticsName, 
			pluginTwo.DiagnosticsName
		);

		pluginOne.PublishDiagnosticsData(new() { ["works"] = pluginOne.Enabled });
		pluginTwo.PublishDiagnosticsData(new() { ["works"] = pluginTwo.Enabled });

		sut.CollectedEvents(pluginOne.DiagnosticsName).Should().ContainSingle().Which
			.Data["works"].Should().Be(pluginOne.Enabled);
		
		sut.CollectedEvents(pluginTwo.DiagnosticsName).Should().ContainSingle().Which
			.Data["works"].Should().Be(pluginTwo.Enabled);
	}
	
	[Fact]
	public void can_handle_diagnostics_data_from_multiple_plugins() {
		using var pluginOne = new TestPlugin(pluginName: Guid.NewGuid().ToString());
		using var pluginTwo = new TestPlugin(pluginName: Guid.NewGuid().ToString());

		using var sut = PluginDiagnosticsDataCollector.Start(
			data => {
				data.Source.Should().BeOneOf(pluginOne.DiagnosticsName, pluginTwo.DiagnosticsName);
				data.Data.TryGetValue("works", out var value).Should().BeTrue();
			}, 
			pluginOne.DiagnosticsName, 
			pluginTwo.DiagnosticsName
		);
	}
	
	[Fact]
	public void can_collect_diagnostics_data_from_multiple_subsystems_plugins() {
		using var pluginOne = new TestSubsystemsPlugin(pluginName: Guid.NewGuid().ToString());
		using var pluginTwo = new TestSubsystemsPlugin(pluginName: Guid.NewGuid().ToString());

		using var sut = PluginDiagnosticsDataCollector.Start(
			pluginOne.DiagnosticsName, 
			pluginTwo.DiagnosticsName
		);

		pluginOne.PublishDiagnosticsData(new() { ["works"] = pluginOne.Enabled });
		pluginTwo.PublishDiagnosticsData(new() { ["works"] = pluginTwo.Enabled });

		sut.CollectedEvents(pluginOne.DiagnosticsName).Should().ContainSingle().Which
			.Data["works"].Should().Be(pluginOne.Enabled);
		
		sut.CollectedEvents(pluginTwo.DiagnosticsName).Should().ContainSingle().Which
			.Data["works"].Should().Be(pluginTwo.Enabled);
	}
	
	[Fact]
	public void can_handle_diagnostics_data_from_multiple_subsystems_plugins() {
		using var pluginOne = new TestSubsystemsPlugin(pluginName: Guid.NewGuid().ToString());
		using var pluginTwo = new TestSubsystemsPlugin(pluginName: Guid.NewGuid().ToString());

		using var sut = PluginDiagnosticsDataCollector.Start(
			data => {
				data.Source.Should().BeOneOf(pluginOne.DiagnosticsName, pluginTwo.DiagnosticsName);
				data.Data.TryGetValue("works", out var value).Should().BeTrue();
			}, 
			pluginOne.DiagnosticsName, 
			pluginTwo.DiagnosticsName
		);
	}

	class TestPlugin(string? pluginName = null) : Plugin(pluginName);

	class TestSubsystemsPlugin(string? pluginName = null) : SubsystemsPlugin(pluginName);
}
