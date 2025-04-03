// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using EventStore.Plugins.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EventStore.Core.Authentication.DelegatedAuthentication;

public class DelegatedAuthenticationProvider(IAuthenticationProvider inner) : AuthenticationProviderBase(new() {
	Name = inner.Name,
	Version = inner.Version,
	LicensePublicKey = inner.LicensePublicKey,
	DiagnosticsName = inner.DiagnosticsName,
	DiagnosticsTags = inner.DiagnosticsTags
})  {
	public IAuthenticationProvider Inner { get; } = inner;

	public override Task Initialize() => Inner.Initialize();

	public override void Authenticate(AuthenticationRequest authenticationRequest) =>
		Inner.Authenticate(new DelegatedAuthenticationRequest(authenticationRequest));

	public override IEnumerable<KeyValuePair<string, string>> GetPublicProperties() =>
		Inner.GetPublicProperties();

	public override void ConfigureEndpoints(IEndpointRouteBuilder endpointRouteBuilder) =>
		Inner.ConfigureEndpoints(endpointRouteBuilder);
	
	public override void ConfigureServices(IServiceCollection services, IConfiguration configuration) =>
		Inner.ConfigureServices(services, configuration);
	
	public override void ConfigureApplication(IApplicationBuilder app, IConfiguration configuration) =>
		Inner.ConfigureApplication(app, configuration);

	public override IReadOnlyList<string> GetSupportedAuthenticationSchemes() =>
		Inner.GetSupportedAuthenticationSchemes();

	class DelegatedAuthenticationRequest(AuthenticationRequest inner) : AuthenticationRequest(inner.Id, inner.Tokens) {
		public override void Unauthorized() => inner.Unauthorized();

		public override void Authenticated(ClaimsPrincipal principal) {
			if (!principal.Identities.Any(identity => identity is DelegatedClaimsIdentity))
				principal.AddIdentity(new DelegatedClaimsIdentity(inner.Tokens));

			inner.Authenticated(principal);
		}

		public override void Error() => inner.Error();

		public override void NotReady() => inner.NotReady();
	}
}
