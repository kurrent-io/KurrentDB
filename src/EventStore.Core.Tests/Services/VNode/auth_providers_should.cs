// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Tests.Helpers;
using EventStore.Plugins.Authentication;
using EventStore.Plugins.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.VNode;

[TestFixture]
public class auth_providers_should : SpecificationWithDirectory {
	[Test]
	public async Task be_registered_with_di() {
		var authenticationConfigured = new TaskCompletionSource();
		var authenticationServicesConfigured = new TaskCompletionSource();
		var authorizationConfigured = new TaskCompletionSource();
		var authorizationServicesConfigured = new TaskCompletionSource();

		await using var node = new MiniNode<LogFormat.V2, string>(
			PathName,
			authenticationProviderFactory: new FakeAuthenticationProviderFactory(authenticationConfigured, authenticationServicesConfigured),
			authorizationProviderFactory: new FakeAuthorizationProviderFactory(authorizationConfigured, authorizationServicesConfigured));

		_ = node.Start();

		await authenticationConfigured.Task.WithTimeout(TimeSpan.FromSeconds(5));
		await authenticationServicesConfigured.Task.WithTimeout(TimeSpan.FromSeconds(5));
		await authorizationConfigured.Task.WithTimeout(TimeSpan.FromSeconds(5));
		await authorizationServicesConfigured.Task.WithTimeout(TimeSpan.FromSeconds(5));
	}
	
	class FakeAuthenticationProviderFactory(TaskCompletionSource configureAppTcs, TaskCompletionSource configureServicesTcs) : IAuthenticationProviderFactory {
		public IAuthenticationProvider Build(bool logFailedAuthenticationAttempts) =>
			new FakeAuthenticationProvider(configureAppTcs, configureServicesTcs);

		class FakeAuthenticationProvider(TaskCompletionSource configureAppTcs, TaskCompletionSource configureServicesTcs) : AuthenticationProviderBase {
			public override void ConfigureServices(IServiceCollection services, IConfiguration configuration) => 
				configureServicesTcs.TrySetResult();

			public override void ConfigureApplication(IApplicationBuilder app, IConfiguration configuration) => 
				configureAppTcs.TrySetResult();

			public override void Authenticate(AuthenticationRequest authenticationRequest) => 
				throw new NotImplementedException();

			public override IReadOnlyList<string> GetSupportedAuthenticationSchemes() =>
				["Basic"];
		}
	}

	class FakeAuthorizationProviderFactory(TaskCompletionSource configureAppTcs, TaskCompletionSource configureServicesTcs) : IAuthorizationProviderFactory {
		public IAuthorizationProvider Build() => new FakeAuthorizationProvider(configureAppTcs, configureServicesTcs);

		class FakeAuthorizationProvider(TaskCompletionSource configureAppTcs, TaskCompletionSource configureServicesTcs) : AuthorizationProviderBase {
			public override void ConfigureServices(IServiceCollection services, IConfiguration configuration) => 
				configureServicesTcs.TrySetResult();

			public override void ConfigureApplication(IApplicationBuilder app, IConfiguration configuration) => 
				configureAppTcs.TrySetResult();

			public override ValueTask<bool> CheckAccessAsync(ClaimsPrincipal cp, Operation operation, CancellationToken ct) => 
				throw new NotImplementedException();
		}
	}
}
