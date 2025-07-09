// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using static System.Convert;

namespace EventStore.Plugins.Licensing;

public record License(JsonWebToken Token) {
	public string? CurrentCultureIgnoreCase { get; private set; }

	public async Task<bool> ValidateAsync(string? publicKey = null) {
		var result = await ValidateTokenAsync(publicKey ?? LicenseConstants.LicensePublicKey, Token.EncodedToken);
		return result.IsValid;
	}

	public async Task<bool> TryValidateAsync(string? publicKey = null) {
		try {
			return await ValidateAsync(publicKey);
		} catch {
			return false;
		}
	}

	public bool HasEntitlements(string[] entitlements, [MaybeNullWhen(true)] out string missing) {
		foreach (var entitlement in entitlements) {
			if (!HasEntitlement(entitlement)) {
				missing = entitlement;
				return false;
			}
		}

		missing = default;
		return true;
	}

	public bool HasEntitlement(string entitlement) {
		foreach (var claim in Token.Claims)
			if (claim.Type.Equals(entitlement, StringComparison.CurrentCultureIgnoreCase) &&
				claim.Value.Equals("true", StringComparison.CurrentCultureIgnoreCase))
				return true;

		return false;
	}

	public static async Task<License> CreateAsync(
		Dictionary<string, object> claims,
		string? publicKey = null,
		string? privateKey = null) {

		publicKey ??= LicenseConstants.LicensePublicKey;
		privateKey ??= LicenseConstants.LicensePrivateKey;

		using var rsa = RSA.Create();
		rsa.ImportRSAPrivateKey(FromBase64String(privateKey), out _);
		var tokenHandler = new JsonWebTokenHandler();
		var token = tokenHandler.CreateToken(new SecurityTokenDescriptor {
			Audience = "esdb",
			Issuer = "esdb",
			Expires = DateTime.UtcNow + TimeSpan.FromHours(1),
			Claims = claims,
			SigningCredentials = new(new RsaSecurityKey(rsa), SecurityAlgorithms.RsaSha256)
		});

		var result = await ValidateTokenAsync(publicKey, token);

		if (!result.IsValid)
			throw new("Token could not be validated");

		if (result.SecurityToken is not JsonWebToken jwt)
			throw new("Token is not a JWT");

		return new(jwt);
	}

	public static License Create(Dictionary<string, object> claims) =>
		CreateAsync(claims).GetAwaiter().GetResult();

	static async Task<TokenValidationResult> ValidateTokenAsync(string publicKey, string token) {
		// not very satisfactory https://github.com/dotnet/runtime/issues/43087
		CryptoProviderFactory.Default.CacheSignatureProviders = false;

		using var rsa = RSA.Create();
		rsa.ImportRSAPublicKey(FromBase64String(publicKey), out _);
		var result = await new JsonWebTokenHandler().ValidateTokenAsync(
			token,
			new() {
				ValidIssuer = "esdb",
				ValidAudience = "esdb",
				IssuerSigningKey = new RsaSecurityKey(rsa),
				ValidateAudience = true,
				ValidateIssuerSigningKey = true,
				ValidateIssuer = true,
				ValidateLifetime = true
			});

		return result;
	}
}
