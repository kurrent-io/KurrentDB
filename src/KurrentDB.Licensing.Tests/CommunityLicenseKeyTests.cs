// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using EventStore.Plugins;
using EventStore.Plugins.Licensing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace KurrentDB.Licensing.Tests;

public class CommunityLicenseKeyTests {
	[Fact]
	public void single_node_without_key_substitutes_single_node_key() {
		var licenseService = CreateLicenseService(isSingleNode: true);

		Exception? error = null;
		licenseService.Licenses.Subscribe(_ => { }, ex => error = ex);

		// "COMMUNITY" was substituted, so the code takes the Keygen path
		// (no emission yet) rather than the NoLicenseKeyException path.
		Assert.Null(error);
	}

	[Fact]
	public void multi_node_without_key_emits_no_license_key_error() {
		var licenseService = CreateLicenseService(isSingleNode: false);

		Exception? error = null;
		licenseService.Licenses.Subscribe(_ => { }, ex => error = ex);

		Assert.IsType<NoLicenseKeyException>(error);
	}

	static ILicenseService CreateLicenseService(bool isSingleNode) {
		var sut = new LicensingPlugin(isSingleNode, ex => { });
		var services = new ServiceCollection();
		services.AddSingleton<IHostApplicationLifetime>(new FakeLifetime());
		var config = new ConfigurationBuilder().Build();

		((IPlugableComponent)sut).ConfigureServices(services, config);

		return services.BuildServiceProvider().GetRequiredService<ILicenseService>();
	}

	class FakeLifetime : IHostApplicationLifetime {
		public CancellationToken ApplicationStarted => CancellationToken.None;
		public CancellationToken ApplicationStopped => CancellationToken.None;
		public CancellationToken ApplicationStopping => CancellationToken.None;
		public void StopApplication() { }
	}
}
