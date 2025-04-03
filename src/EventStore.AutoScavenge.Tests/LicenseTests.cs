// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using EventStore.Plugins.Licensing;

namespace EventStore.AutoScavenge.Tests;

public class LicenseTests {
	[Fact]
	public void when_invalid_license_disable() {
		var startup = new DummyStartup(new ConfigurationBuilder().Build());
		var sut = startup.AutoScavengePlugin;

		var builder = WebApplication.CreateBuilder();

		startup.ConfigureServices(builder.Services);
		startup.Configure(builder.Build());
		Assert.True(sut.Enabled);

		var tokenWithoutEntitlement = "eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9.eyJhdWQiOiJlc2RiIiwiaXNzIjoiZXNkYiIsImV4cCI6MTgyNDQ1NjgyOSwianRpIjoiOTVmZTY2YzAtMDRkMi00MjExLWI1ZGQtNTAyM2MyYTAxMGFiIiwic3ViIjoiRVNEQiBUZXN0cyIsIklzVHJpYWwiOiJUcnVlIiwiSXNFeHBpcmVkIjoiRmFsc2UiLCJJc1ZhbGlkIjoiVHJ1ZSIsIklzRmxvYXRpbmciOiJUcnVlIiwiRGF5c1JlbWFpbmluZyI6IjEiLCJTdGFydERhdGUiOiIyNi8wNC8yMDI0IDAwOjAwOjAwICswMTowMCIsIk5PTkUiOiJ0cnVlIiwiaWF0IjoxNzI5ODQ4ODI5LCJuYmYiOjE3Mjk4NDg4Mjl9.R24i-ZAow3BhRaST3n25Uc_nQ184k83YRZZ0oRcWbU9B9XNLRH0Iegj0HmkyzkT50I4gcIJOIfcO6mIPp4Y959CP7aTAlt7XEnXoGF0GwsfXatAxy4iXG8Gpya7INgMoWEeN0v8eDH8_OVmnieOxeba9ex5j1oAW_FtQDMzcFjAeErpW__8zmkCsn6GzvlhdLE4e3r2wjshvrTTcS_1fvSVjQZov5ce2sVBJPegjCLO_QGiIBK9QTnpHrhe6KCYje6fSTjgty0V1Qj22bftvrXreYzQijPrnC_ek1BwV-A1JvacZugMCPIy8WvE5jE3hVYRWGGUzQZ-CibPGsjudYA";
		startup.LicenseService.EmitLicense(new License(new(tokenWithoutEntitlement)));
		Assert.False(sut.Enabled);
	}

	[Fact]
	public void when_valid_license_stay_enabled() {
		var startup = new DummyStartup(new ConfigurationBuilder().Build());
		var sut = startup.AutoScavengePlugin;

		var builder = WebApplication.CreateBuilder();

		startup.ConfigureServices(builder.Services);
		startup.Configure(builder.Build());
		Assert.True(sut.Enabled);

		startup.LicenseService.EmitLicense(startup.LicenseService.CurrentLicense!);
		Assert.True(sut.Enabled);
	}
}
