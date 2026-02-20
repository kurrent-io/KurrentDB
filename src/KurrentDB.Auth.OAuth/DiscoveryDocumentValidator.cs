// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable

using System;
using System.Linq;
using IdentityModel;
using IdentityModel.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace KurrentDB.Auth.OAuth;

public static class DiscoveryDocumentValidator {
	private const string AuthorizationCodeResponseType = OidcConstants.ResponseTypes.Code;
	private const string AuthorizationCodeGrantType = OidcConstants.GrantTypes.AuthorizationCode;
	private const string SHA256CodeChallengeMethod = OidcConstants.CodeChallengeMethods.Sha256;

	public static void Validate(
		DiscoveryDocumentResponse disco,
		OAuthAuthenticationPlugin.Settings settings,
		out string authorizationEndpoint,
		out string tokenEndpoint,
		out string codeChallengeMethod,
		ILogger? logger = null) {

		logger ??= NullLogger.Instance;

		if (disco.IsError) {
			throw new Exception(disco.Error);
		}

		authorizationEndpoint = disco.AuthorizeEndpoint ?? throw new Exception("Authorization Endpoint is null in identity provider's discovery document.");
		tokenEndpoint = disco.TokenEndpoint ?? throw new Exception("Token Endpoint is null in identity provider's discovery document.");

		// required field according to the specs
		if (!disco.ResponseTypesSupported.Contains(AuthorizationCodeResponseType)) {
			throw new Exception($"The specified identity provider does not support the '{AuthorizationCodeResponseType}' response type");
		}

		// the specs say: If omitted, the default value is ["authorization_code", "implicit"].
		// https://datatracker.ietf.org/doc/html/rfc8414#section-2 see grant_types_supported
		if (disco.GrantTypesSupported.Any() && !disco.GrantTypesSupported.Contains(AuthorizationCodeGrantType)) {
			throw new Exception($"The specified identity provider does not support the '{AuthorizationCodeGrantType}' grant type");
		}

		//the specs say: If omitted, the authorization server does not support PKCE
		// https://datatracker.ietf.org/doc/html/rfc8414#section-2 see code_challenge_methods_supported
		codeChallengeMethod = SHA256CodeChallengeMethod;
		if (settings.DisableCodeChallengeMethodsSupportedValidation) {
			logger.LogInformation("Skipping code_challenge_methods_supported validation. Using {CodeChallengeMethod}", SHA256CodeChallengeMethod);
		} else {
			if (!disco.CodeChallengeMethodsSupported.Any()) {
				throw new Exception($"The specified identity provider does not support PKCE. If using Microsoft Entra, set the KurrentDB DisableCodeChallengeMethodsSupportedValidation OAuth flag.");
			}

			if (!disco.CodeChallengeMethodsSupported.Contains(SHA256CodeChallengeMethod)) {
				throw new Exception($"The specified identity provider does not support the '{SHA256CodeChallengeMethod}' code challenge method");
			}
		}
	}
}
