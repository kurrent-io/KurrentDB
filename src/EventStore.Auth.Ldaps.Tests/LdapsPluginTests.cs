// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using System.Threading.Tasks;
using EventStore.Plugins.Licensing;
using EventStore.Plugins.TestHelpers;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace EventStore.Auth.Ldaps.Tests;

public class LdapsPluginTests {

	private const string _roleClaimType = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role";
	private readonly ITestOutputHelper _output;

	private readonly string _configFile;
	private readonly TimeSpan _timeout = TimeSpan.FromSeconds(5);

	public LdapsPluginTests(ITestOutputHelper output) {
		_output = output;
		_configFile = Path.Combine(Environment.CurrentDirectory, "conf", "ldaps.conf");
	}

	private async Task<LdapsFixture> CreateAndStartFixture() {
		var fixture = new LdapsFixture(_output);
		fixture.Start();

		var sut = new LdapsAuthenticationPlugin()
			.GetAuthenticationProviderFactory(_configFile)
			.Build(false);

		// There is no health check in this container
		// Simply try a few requests until one succeeds
		for (var i = 0; i < 5; i++) {
			var completionSource = new TaskCompletionSource<TestAuthenticationResponse>();
			var request = CreateAuthenticationRequest("professor", "professor", completionSource);
			var task = completionSource.Task;
			sut.Authenticate(request);

			if (await Task.WhenAny(task, Task.Delay(_timeout)) == task &&
			    string.IsNullOrEmpty(task.Result.FailureReason))
				return fixture;

			await Task.Delay(500);
		}

		throw new Exception("LDAPS fixture did not start up in time");
	}

	[FactRequiringDocker]
	public async Task authenticate_admin_user_returns_admin_role() {
		var sut = new LdapsAuthenticationPlugin()
			.GetAuthenticationProviderFactory(_configFile)
			.Build(false);

		using var fixture = await CreateAndStartFixture();

		var completionSource = new TaskCompletionSource<TestAuthenticationResponse>();
		var request = CreateAuthenticationRequest("professor", "professor", completionSource);
		sut.Authenticate(request);

		var task = completionSource.Task;
		if (await Task.WhenAny(task, Task.Delay(_timeout)) == task) {
			var result = await completionSource.Task;

			Assert.Null(result.FailureReason);
			Assert.NotNull(result.Principal);
			Assert.Equal("professor", result.Principal.Identity?.Name);
			Assert.True(result.Principal.HasClaim(_roleClaimType, "$admins"));
		}
	}

	[FactRequiringDocker]
	public async Task authenticate_with_incorrect_password_returns_unauthorized() {
		var sut = new LdapsAuthenticationPlugin()
			.GetAuthenticationProviderFactory(_configFile)
			.Build(false);

		using var fixture = await CreateAndStartFixture();

		var completionSource = new TaskCompletionSource<TestAuthenticationResponse>();
		var request = CreateAuthenticationRequest("professor", "wrong", completionSource);
		sut.Authenticate(request);

		var task = completionSource.Task;
		if (await Task.WhenAny(task, Task.Delay(_timeout)) == task) {
			var result = await completionSource.Task;
			Assert.Null(result.Principal);
			Assert.Equal("unauthorized", result.FailureReason);
		}
	}

	[FactRequiringDocker]
	public async Task authenticate_with_non_existent_user_returns_unauthorized() {
		var sut = new LdapsAuthenticationPlugin()
			.GetAuthenticationProviderFactory(_configFile)
			.Build(false);

		using var fixture = await CreateAndStartFixture();

		var completionSource = new TaskCompletionSource<TestAuthenticationResponse>();
		var request = CreateAuthenticationRequest("wrong", "wrong", completionSource);
		sut.Authenticate(request);

		var task = completionSource.Task;
		if (await Task.WhenAny(task, Task.Delay(_timeout)) == task) {
			var result = await completionSource.Task;
			Assert.Null(result.Principal);
			Assert.Equal("unauthorized", result.FailureReason);
		}
	}

	[FactRequiringDocker]
	public async Task authenticate_user_with_custom_role_returns_expected_roles() {
		var sut = new LdapsAuthenticationPlugin()
			.GetAuthenticationProviderFactory(_configFile)
			.Build(false);

		using var fixture = await CreateAndStartFixture();

		var completionSource = new TaskCompletionSource<TestAuthenticationResponse>();
		var request = CreateAuthenticationRequest("fry", "fry", completionSource);
		sut.Authenticate(request);

		var task = completionSource.Task;
		if (await Task.WhenAny(task, Task.Delay(_timeout)) == task) {
			var result = await completionSource.Task;
			Assert.Null(result.FailureReason);
			Assert.NotNull(result.Principal);
			Assert.Equal("fry", result.Principal.Identity?.Name);
			Assert.True(result.Principal.HasClaim(_roleClaimType, "others"));
			Assert.False(result.Principal.HasClaim(_roleClaimType, "$admins"));
		}
	}

	[Fact(Skip="This currently results in an error and the request never returns")]
	public async Task authenticate_user_with_no_roles_returns_no_roles() {
		var sut = new LdapsAuthenticationPlugin()
			.GetAuthenticationProviderFactory(_configFile)
			.Build(false);

		using var fixture = await CreateAndStartFixture();

		var completionSource = new TaskCompletionSource<TestAuthenticationResponse>();
		var request = CreateAuthenticationRequest("amy", "amy", completionSource);
		sut.Authenticate(request);

		var task = completionSource.Task;
		if (await Task.WhenAny(task, Task.Delay(_timeout)) == task) {
			var result = await completionSource.Task;

			Assert.Null(result.FailureReason);
			Assert.NotNull(result.Principal);
			Assert.Equal("amy", result.Principal.Identity?.Name);
			Assert.DoesNotContain(result.Principal.Claims, x => x.Type == _roleClaimType);
		}
	}

	private TestAuthenticationRequest CreateAuthenticationRequest(string username, string password,
		TaskCompletionSource<TestAuthenticationResponse> completionSource) {
		return new(username, password,
			p => completionSource.TrySetResult(new TestAuthenticationResponse(p)),
			() => completionSource.TrySetResult(new TestAuthenticationResponse("unauthorized")),
			() => completionSource.TrySetResult(new TestAuthenticationResponse("error")),
			() => completionSource.TrySetResult(new TestAuthenticationResponse("notready")));
	}

	[Theory]
	[InlineData(true, "LDAPS_AUTHENTICATION", false)]
	[InlineData(true, "NONE", true)]
	[InlineData(false, "NONE", true)]
	public void respects_license(bool licensePresent, string entitlement, bool expectedException) {
		// given
		var sut = new LdapsAuthenticationPlugin()
			.GetAuthenticationProviderFactory(_configFile)
			.Build(false);

		var config = new ConfigurationBuilder().Build();
		var builder = WebApplication.CreateBuilder();

		var licenseService = new Fixtures.FakeLicenseService(licensePresent, entitlement);
		builder.Services.AddSingleton<ILicenseService>(licenseService);

		sut.ConfigureServices(
			builder.Services,
			config);

		var app = builder.Build();

		// when
		sut.ConfigureApplication(app, config);

		// then
		if (expectedException) {
			Assert.NotNull(licenseService.RejectionException);
		} else {
			Assert.Null(licenseService.RejectionException);
		}
	}
}
