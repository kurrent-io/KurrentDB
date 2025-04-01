// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Plugins.Authorization;
using EventStore.Plugins.Licensing;
using EventStore.Plugins.TestHelpers;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace EventStore.Auth.LegacyAuthorizationWithStreamAuthorizationDisabled.Tests;

public class LegacyAuthorizationWithStreamAuthorizationDisabledPluginTests {
	private readonly ITestOutputHelper _outputHelper;

	public LegacyAuthorizationWithStreamAuthorizationDisabledPluginTests(ITestOutputHelper outputHelper) {
		_outputHelper = outputHelper;
	}

	public static IEnumerable<object[]> TestCases() {
		yield return new object[] {new[] {new Claim(ClaimTypes.Role, SystemRoles.Admins)}};
		yield return new object[] {Array.Empty<Claim>()};
	}

	[Theory, MemberData(nameof(TestCases))]
	public async Task read_all_stream(Claim[] claims) {
		var configurationPath = Path.GetTempFileName();

		try {
			var sut = new LegacyAuthorizationWithStreamAuthorizationDisabledPlugin();
			var provider = sut.GetAuthorizationProviderFactory(configurationPath).Build();

			var result = await provider.CheckAccessAsync(
				new ClaimsPrincipal(new ClaimsIdentity(claims)),
				new Operation(Operations.Streams.Read).WithParameter(
					Operations.Streams.Parameters.StreamId("$all")), CancellationToken.None);

			Assert.True(result);
		} finally {
			File.Delete(configurationPath);
		}
	}
	
	[Theory, MemberData(nameof(TestCases))]
	public async Task write_all_stream(Claim[] claims) {
		var configurationPath = Path.GetTempFileName();

		try {
			var sut = new LegacyAuthorizationWithStreamAuthorizationDisabledPlugin();
			var provider = sut.GetAuthorizationProviderFactory(configurationPath).Build();

			var result = await provider.CheckAccessAsync(
				new ClaimsPrincipal(new ClaimsIdentity(claims)),
				new Operation(Operations.Streams.Write).WithParameter(
					Operations.Streams.Parameters.StreamId("$all")), CancellationToken.None);

			Assert.False(result);
		} finally {
			File.Delete(configurationPath);
		}
	}

	[Theory, MemberData(nameof(TestCases))]
	public async Task create_user(Claim[] claims) {
		var configurationPath = Path.GetTempFileName();

		try {
			var sut = new LegacyAuthorizationWithStreamAuthorizationDisabledPlugin();
			var provider = sut.GetAuthorizationProviderFactory(configurationPath).Build();

			var result = await provider.CheckAccessAsync(
				new ClaimsPrincipal(new ClaimsIdentity(claims)),
				new Operation(Operations.Users.Create), CancellationToken.None);

			Assert.Equal(claims.Length > 0, result);
		} finally {
			File.Delete(configurationPath);
		}
	}

	[Theory]
	[InlineData(true, "LEGACY_AUTHORIZATION_WITH_STREAM_AUTHORIZATION_DISABLED", false)]
	[InlineData(true, "NONE", true)]
	[InlineData(false, "NONE", true)]
	public void respects_license(bool licensePresent, string entitlement, bool expectedException) {
		var configurationPath = Path.GetTempFileName();

		try {
			// given
			var sut = new LegacyAuthorizationWithStreamAuthorizationDisabledPlugin();
			var provider = sut.GetAuthorizationProviderFactory(configurationPath).Build();
			var config = new ConfigurationBuilder().Build();
			var builder = WebApplication.CreateBuilder();

			var licenseService = new Fixtures.FakeLicenseService(licensePresent, entitlement);
			builder.Services.AddSingleton<ILicenseService>(licenseService);

			provider.ConfigureServices(
				builder.Services,
				config);

			var app = builder.Build();

			// when
			provider.ConfigureApplication(app, config);

			// then
			if (expectedException) {
				Assert.NotNull(licenseService.RejectionException);
			} else {
				Assert.Null(licenseService.RejectionException);
			}
		} finally {
			File.Delete(configurationPath);
		}
	}
}
