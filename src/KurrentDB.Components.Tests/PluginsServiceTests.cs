// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.Linq;
using EventStore.Plugins;
using KurrentDB.Components.Plugins;
using KurrentDB.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KurrentDB.Components.Tests;

// PluginsService has no authorization layer to assert — it reads the in-process pluggable-component inventory
// from ClusterVNodeOptions (the Plugins page itself is gated by [Authorize(Policy = ViewSubsystems)]). What it
// *does* own is the data the page binds to: the list is sorted by name and IsPluginEnabled answers the
// presence + enabled question other pages use as a feature gate. Those are what these tests pin down.
public class PluginsServiceTests {
	static PluginsService Make(params IPlugableComponent[] plugins) =>
		new(new ClusterVNodeOptions { PlugableComponents = plugins });

	[Fact]
	public void Lists_pluggable_components_sorted_by_name() {
		var svc = Make(new FakePlugin("Zeta"), new FakePlugin("Alpha"), new FakePlugin("Mu"));

		Assert.Equal(["Alpha", "Mu", "Zeta"], svc.PluggableComponents.Select(x => x.Name));
	}

	[Theory]
	[InlineData("Alpha", true)]    // present and enabled
	[InlineData("Beta", false)]    // present but disabled
	[InlineData("Missing", false)] // absent
	public void IsPluginEnabled_reflects_presence_and_enabled_flag(string name, bool expected) {
		var svc = Make(new FakePlugin("Alpha", enabled: true), new FakePlugin("Beta", enabled: false));

		Assert.Equal(expected, svc.IsPluginEnabled(name));
	}

	// Minimal IPlugableComponent; only Name/Enabled are consulted by PluginsService.
	sealed class FakePlugin(string name, bool enabled = true) : IPlugableComponent {
		public string Name => name;
		public bool Enabled => enabled;
		public string DiagnosticsName => name;
		public KeyValuePair<string, object?>[] DiagnosticsTags => [];
		public string Version => "1.0.0";
		public string LicensePublicKey => "";
		public void ConfigureServices(IServiceCollection services, IConfiguration configuration) { }
		public void ConfigureApplication(IApplicationBuilder builder, IConfiguration configuration) { }
	}
}
