// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Microsoft.AspNetCore.Routing;

namespace EventStore.Plugins.Authentication;

public interface IAuthenticationProvider : IPlugableComponent {
	/// <summary>
	///     Initialize the AuthenticationProvider. Event Store will wait until this task completes before becoming ready.
	/// </summary>
	Task Initialize();

	/// <summary>
	///     Authenticate an AuthenticationRequest. Call the appropriate method on <see cref="AuthenticationRequest" />
	///     depending on whether the request succeeded, failed, or errored.
	/// </summary>
	/// <param name="authenticationRequest"></param>
	void Authenticate(AuthenticationRequest authenticationRequest);

	/// <summary>
	///     Get public properties which may be required for the authentication flow.
	/// </summary>
	IEnumerable<KeyValuePair<string, string>> GetPublicProperties();

	/// <summary>
	///     Create any required endpoints.
	/// </summary>
	/// <param name="endpointRouteBuilder"></param>
	void ConfigureEndpoints(IEndpointRouteBuilder endpointRouteBuilder);

	/// <summary>
	///     Get supported authentication schemes.
	/// </summary>
	IReadOnlyList<string> GetSupportedAuthenticationSchemes();
}
