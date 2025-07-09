// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Security.Claims;

namespace EventStore.Plugins.Authentication;

public abstract class AuthenticationRequest {
	/// <summary>
	///     Whether a valid client certificate was supplied with the request
	/// </summary>
	public readonly bool HasValidClientCertificate;

	/// <summary>
	///     The Identifier for the source that this request came from
	/// </summary>
	public readonly string Id;

	/// <summary>
	///     The name of the principal for the request
	/// </summary>
	public readonly string Name;

	/// <summary>
	///     The supplied password for the request
	/// </summary>
	public readonly string SuppliedPassword;

	/// <summary>
	///     All supplied authentication tokens for the request
	/// </summary>
	public readonly IReadOnlyDictionary<string, string> Tokens;

	protected AuthenticationRequest(string? id, IReadOnlyDictionary<string, string>? tokens) {
		ArgumentNullException.ThrowIfNull(id);
		ArgumentNullException.ThrowIfNull(tokens);

		Id = id;
		Tokens = tokens;
		Name = GetToken("uid") ?? "";
		SuppliedPassword = GetToken("pwd") ?? "";
		HasValidClientCertificate = GetToken("client-certificate") != null;
	}

	/// <summary>
	///     Gets the token corresponding to <param name="key" />.
	/// </summary>
	/// <param name="key"></param>
	/// <returns></returns>
	public string? GetToken(string key) => Tokens.GetValueOrDefault(key);

	/// <summary>
	///     The request is unauthorized
	/// </summary>
	public abstract void Unauthorized();

	/// <summary>
	///     The request was successfully authenticated
	/// </summary>
	/// <param name="principal">The <see cref="ClaimsPrincipal" /> of the authenticated request</param>
	public abstract void Authenticated(ClaimsPrincipal principal);

	/// <summary>
	///     An error occurred during authentication
	/// </summary>
	public abstract void Error();

	/// <summary>
	///     The authentication provider is not yet ready to service the request
	/// </summary>
	public abstract void NotReady();
}
