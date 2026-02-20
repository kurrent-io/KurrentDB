// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable

using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using IdentityModel.Client;
using Xunit;

namespace KurrentDB.Auth.OAuth.Tests;

public class DiscoveryDocumentValidatorTests {
	private static OAuthAuthenticationPlugin.Settings DefaultSettings => new() {
		DisableCodeChallengeMethodsSupportedValidation = false,
	};

	private static OAuthAuthenticationPlugin.Settings SkipPkceSettings => new() {
		DisableCodeChallengeMethodsSupportedValidation = true,
	};

	[Fact]
	public async Task valid_discovery_document_does_not_throw() {
		var disco = await CreateDiscoveryResponse(new DiscoDocument());

		DiscoveryDocumentValidator.Validate(disco, DefaultSettings,
			out var authorizationEndpoint, out var tokenEndpoint, out var codeChallengeMethod);

		Assert.Equal("https://idp.example.com/authorize", authorizationEndpoint);
		Assert.Equal("https://idp.example.com/token", tokenEndpoint);
		Assert.Equal("S256", codeChallengeMethod);
	}

	[Fact]
	public async Task error_response_throws() {
		var disco = await CreateDiscoveryResponse(statusCode: HttpStatusCode.InternalServerError);

		var ex = Assert.Throws<Exception>(() =>
			DiscoveryDocumentValidator.Validate(disco, DefaultSettings, out _, out _, out _));
		Assert.Contains("Error", ex.Message, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public async Task missing_authorize_endpoint_throws() {
		var disco = await CreateDiscoveryResponse(new DiscoDocument {
			AuthorizationEndpoint = null
		});

		var ex = Assert.Throws<Exception>(() =>
			DiscoveryDocumentValidator.Validate(disco, DefaultSettings, out _, out _, out _));
		Assert.Contains("Malformed endpoint:", ex.Message);
	}

	[Fact]
	public async Task missing_token_endpoint_throws() {
		var disco = await CreateDiscoveryResponse(new DiscoDocument {
			TokenEndpoint = null
		});

		var ex = Assert.Throws<Exception>(() =>
			DiscoveryDocumentValidator.Validate(disco, DefaultSettings, out _, out _, out _));
		Assert.Contains("Malformed endpoint:", ex.Message);
	}

	[Fact]
	public async Task missing_code_response_type_throws() {
		var disco = await CreateDiscoveryResponse(new DiscoDocument {
			ResponseTypesSupported = ["token"]
		});

		var ex = Assert.Throws<Exception>(() =>
			DiscoveryDocumentValidator.Validate(disco, DefaultSettings, out _, out _, out _));
		Assert.Contains("code", ex.Message);
		Assert.Contains("response type", ex.Message);
	}

	[Fact]
	public async Task missing_authorization_code_grant_type_throws() {
		var disco = await CreateDiscoveryResponse(new DiscoDocument {
			GrantTypesSupported = ["implicit"]
		});

		var ex = Assert.Throws<Exception>(() =>
			DiscoveryDocumentValidator.Validate(disco, DefaultSettings, out _, out _, out _));
		Assert.Contains("authorization_code", ex.Message);
		Assert.Contains("grant type", ex.Message);
	}

	[Fact]
	public async Task empty_grant_types_does_not_throw() {
		// Per spec, if omitted, the default is ["authorization_code", "implicit"]
		var disco = await CreateDiscoveryResponse(new DiscoDocument {
			GrantTypesSupported = []
		});

		DiscoveryDocumentValidator.Validate(disco, DefaultSettings, out _, out _, out _);
	}

	[Fact]
	public async Task no_code_challenge_methods_throws() {
		var disco = await CreateDiscoveryResponse(new DiscoDocument {
			CodeChallengeMethodsSupported = []
		});

		var ex = Assert.Throws<Exception>(() =>
			DiscoveryDocumentValidator.Validate(disco, DefaultSettings, out _, out _, out _));
		Assert.Contains("PKCE", ex.Message);
		Assert.Contains("Microsoft Entra", ex.Message);
	}

	[Fact]
	public async Task missing_s256_code_challenge_method_throws() {
		var disco = await CreateDiscoveryResponse(new DiscoDocument {
			CodeChallengeMethodsSupported = ["plain"]
		});

		var ex = Assert.Throws<Exception>(() =>
			DiscoveryDocumentValidator.Validate(disco, DefaultSettings, out _, out _, out _));
		Assert.Contains("S256", ex.Message);
		Assert.Contains("code challenge method", ex.Message);
	}

	[Fact]
	public async Task disable_pkce_validation_skips_code_challenge_check() {
		var disco = await CreateDiscoveryResponse(new DiscoDocument {
			CodeChallengeMethodsSupported = []
		});

		DiscoveryDocumentValidator.Validate(disco, SkipPkceSettings, out _, out _, out _);
	}

	[Fact]
	public async Task disable_pkce_validation_skips_s256_check() {
		var disco = await CreateDiscoveryResponse(new DiscoDocument {
			CodeChallengeMethodsSupported = ["plain"]
		});

		DiscoveryDocumentValidator.Validate(disco, SkipPkceSettings, out _, out _, out _);
	}

	private static async Task<DiscoveryDocumentResponse> CreateDiscoveryResponse(
		DiscoDocument? doc = null,
		HttpStatusCode statusCode = HttpStatusCode.OK) {

		doc ??= new();

		var json = JsonSerializer.Serialize(doc);
		var httpResponse = new HttpResponseMessage(statusCode) {
			Content = new StringContent(json, Encoding.UTF8, "application/json"),
		};

		var policy = new DiscoveryPolicy {
			Authority = "https://idp.example.com",
		};

		return await ProtocolResponse.FromHttpResponseAsync<DiscoveryDocumentResponse>(httpResponse, policy);
	}

	private class DiscoDocument {
		[JsonPropertyName("issuer")]
		public string Issuer { get; set; } = "https://idp.example.com";

		[JsonPropertyName("authorization_endpoint")]
		public string? AuthorizationEndpoint { get; set; } = "https://idp.example.com/authorize";

		[JsonPropertyName("token_endpoint")]
		public string? TokenEndpoint { get; set; } = "https://idp.example.com/token";

		[JsonPropertyName("jwks_uri")]
		public string JwksUri { get; set; } = "https://idp.example.com/.well-known/jwks";

		[JsonPropertyName("response_types_supported")]
		public string[] ResponseTypesSupported { get; set; } = ["code"];

		[JsonPropertyName("grant_types_supported")]
		public string[] GrantTypesSupported { get; set; } = ["authorization_code"];

		[JsonPropertyName("code_challenge_methods_supported")]
		public string[] CodeChallengeMethodsSupported { get; set; } = ["S256"];
	}
}
