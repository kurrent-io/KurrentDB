// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using EventStore.Plugins.Licensing;
using Xunit;

namespace KurrentDB.Licensing.Tests;

public class SingleNodeFallbackLicenseProviderTests {
	static Task<License> ValidLicenseAsync() =>
		License.CreateAsync(new() { ["MY_FEATURE"] = "true" });

	static string Notes(License license) =>
		license.Token.Claims.Single(c => c.Type == "notes").Value;

	[Fact]
	public async Task multi_node_passes_through_valid_license() {
		var inner = new AdHocLicenseProvider(await ValidLicenseAsync());
		var sut = new SingleNodeFallbackLicenseProvider(inner, isSingleNode: false);

		var license = await sut.Licenses.FirstAsync();

		Assert.True(license.HasEntitlement("MY_FEATURE"));
		Assert.False(license.HasEntitlement("ALL"));
	}

	[Fact]
	public async Task multi_node_propagates_error() {
		var inner = new AdHocLicenseProvider(new Exception("no usable license"));
		var sut = new SingleNodeFallbackLicenseProvider(inner, isSingleNode: false);

		var ex = await Assert.ThrowsAsync<Exception>(async () => await sut.Licenses.FirstAsync());
		Assert.Equal("no usable license", ex.Message);
	}

	[Fact]
	public async Task single_node_passes_through_valid_license() {
		var inner = new AdHocLicenseProvider(await ValidLicenseAsync());
		var sut = new SingleNodeFallbackLicenseProvider(inner, isSingleNode: true);

		var license = await sut.Licenses.FirstAsync();

		Assert.True(license.HasEntitlement("MY_FEATURE"));
		Assert.False(license.HasEntitlement("ALL"));
	}

	[Fact]
	public async Task single_node_grants_all_when_no_license_key() {
		var inner = new AdHocLicenseProvider(new NoLicenseKeyException());
		var sut = new SingleNodeFallbackLicenseProvider(inner, isSingleNode: true);

		var license = await sut.Licenses.FirstAsync();

		Assert.True(license.HasEntitlement("ALL"));
		Assert.Contains("No license key", Notes(license));
	}

	[Fact]
	public async Task single_node_grants_all_when_license_invalid() {
		var inner = new AdHocLicenseProvider(new Exception("license expired"));
		var sut = new SingleNodeFallbackLicenseProvider(inner, isSingleNode: true);

		var license = await sut.Licenses.FirstAsync();

		Assert.True(license.HasEntitlement("ALL"));
		Assert.Contains("could not be validated", Notes(license));
	}
}
