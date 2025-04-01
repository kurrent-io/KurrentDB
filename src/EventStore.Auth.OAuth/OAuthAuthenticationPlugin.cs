// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#pragma warning disable CS8601 // Possible null reference assignment.
#pragma warning disable CS8602 // Dereference of a possibly null reference.

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using EventStore.Plugins;
using EventStore.Plugins.Authentication;
using IdentityModel;
using IdentityModel.Client;
using LruCacheNet;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using Serilog;

#nullable enable
namespace EventStore.Auth.OAuth;

[Export(typeof(IAuthenticationPlugin))]
public class OAuthAuthenticationPlugin : IAuthenticationPlugin {
	public static readonly string[] ValidSigningAlgorithms = {
		SecurityAlgorithms.RsaSha256Signature,
		SecurityAlgorithms.RsaSha384Signature,
		SecurityAlgorithms.RsaSha512Signature,
		SecurityAlgorithms.RsaSsaPssSha256Signature,
		SecurityAlgorithms.RsaSsaPssSha384Signature,
		SecurityAlgorithms.RsaSsaPssSha512Signature,
		SecurityAlgorithms.RsaSha256,
		SecurityAlgorithms.RsaSha384,
		SecurityAlgorithms.RsaSha512,
		SecurityAlgorithms.RsaSsaPssSha256,
		SecurityAlgorithms.RsaSsaPssSha384,
		SecurityAlgorithms.RsaSsaPssSha512,
	};

	private static readonly ILogger _logger = Log.ForContext<OAuthAuthenticationPlugin>();
	public string Name { get; } = "OAUTH";
	public string Version { get; } = typeof(OAuthAuthenticationPlugin).Assembly.GetName().Version!.ToString();
	public string CommandLineName { get; } = "oauth";

	public IAuthenticationProviderFactory GetAuthenticationProviderFactory(string authenticationConfigPath)
		=> new OAuthAuthenticationProviderFactory(authenticationConfigPath);

	private class OAuthAuthenticationProviderFactory : IAuthenticationProviderFactory {
		private readonly string _authenticationConfigPath;

		public OAuthAuthenticationProviderFactory(string authenticationConfigPath) {
			if (authenticationConfigPath == null) {
				throw new ArgumentNullException(nameof(authenticationConfigPath));
			}

			if (!File.Exists(authenticationConfigPath)) {
				throw new FileNotFoundException(null, authenticationConfigPath);
			}

			_authenticationConfigPath = authenticationConfigPath;
		}

		public IAuthenticationProvider Build(bool logFailedAuthenticationAttempts)
			=> new OAuthAuthenticationProvider(new Options {
				LogFailedAuthenticationAttempts = logFailedAuthenticationAttempts,
				Settings = ConfigParser.ReadConfiguration<Settings>(_authenticationConfigPath, "OAuth")
					?? throw new Exception("Could not read OAuth configuration")
			});
	}

	private class OAuthAuthenticationProvider : AuthenticationProviderBase {
		private readonly JwtSecurityTokenHandler _securityTokenHandler;
		private readonly Options _options;
		private AsymmetricSecurityKey[] _signingKeys;
		private string _signingKeysUri;
		private DateTime _signingKeysLastRefresh;
		private readonly TimeSpan _signingKeysMinRefreshInterval = TimeSpan.FromMinutes(30);
		private readonly object _signingKeysRefreshLock = new object();
		private const string AuthorizationCodeResponseType = OidcConstants.ResponseTypes.Code;
		private const string AuthorizationCodeGrantType = OidcConstants.GrantTypes.AuthorizationCode;
		private const string SHA256CodeChallengeMethod = OidcConstants.CodeChallengeMethods.Sha256;
		private const string CallBackUrl = "/oauth/callback";
		private const string CodeChallengeUrl = "/oauth/codechallenge";
		private string _authorizationEndpoint;
		private string _tokenEndpoint;
		private readonly HttpClient _httpClient;
		private readonly RandomNumberGenerator _rngCsp;
		private readonly LruCache<string, string> _codeVerifierLruCache;
		private volatile bool _ready;

		public OAuthAuthenticationProvider(Options options)
			: base(
				requiredEntitlements: ["OAUTH_AUTHENTICATION"]) {

			_options = options;
			_signingKeys = new AsymmetricSecurityKey[0];
			_signingKeysUri = string.Empty;
			_securityTokenHandler = new JwtSecurityTokenHandler();
			_authorizationEndpoint = string.Empty;
			_tokenEndpoint = string.Empty;
			_httpClient = new HttpClient(new SocketsHttpHandler {
				ConnectTimeout = TimeSpan.FromSeconds(10),
				SslOptions = {
					RemoteCertificateValidationCallback = _options.Settings.Insecure
						? delegate { return true; }
						: (RemoteCertificateValidationCallback)null!
				}
			});

			_rngCsp = RandomNumberGenerator.Create();
			_codeVerifierLruCache = new LruCache<string, string>(1024);
		}

		public override async Task Initialize() {
			try {
				_logger.Information("Obtaining auth token signing key from {idp}",
					_options.Settings.IdpUri);
				using var httpClient = new HttpClient(new SocketsHttpHandler {
					SslOptions = {
						RemoteCertificateValidationCallback = _options.Settings.Insecure
							? delegate { return true; }
							: (RemoteCertificateValidationCallback)null!
					}
				}) {
					BaseAddress = _options.Settings.IdpUri
				};

				var request = new DiscoveryDocumentRequest {
					Policy = new DiscoveryPolicy {
						AdditionalEndpointBaseAddresses = _options.Settings.AdditionalEndpointBaseAddresses
					}
				};

				var disco = await httpClient.GetDiscoveryDocumentAsync(request);

				if (disco.IsError) {
					throw new Exception(disco.Error);
				}

				_authorizationEndpoint = disco.AuthorizeEndpoint ?? throw new Exception("Authorization Endpoint is null in identity provider's discovery document.");
				_tokenEndpoint = disco.TokenEndpoint ?? throw new Exception("Token Endpoint is null in identity provider's discovery document.");

				// required field according to the specs
				if (!disco.ResponseTypesSupported.Contains(AuthorizationCodeResponseType)) {
					throw new Exception($"The specified identity provider does not support the '{AuthorizationCodeResponseType}' response type");
				}

				// the specs say: If omitted, the default value is ["authorization_code", "implicit"].
				if (disco.GrantTypesSupported.Any() && !disco.GrantTypesSupported.Contains(AuthorizationCodeGrantType)) {
					throw new Exception($"The specified identity provider does not support the '{AuthorizationCodeGrantType}' grant type");
				}

				//the specs say: If omitted, the authorization server does not support PKCE
				if (!disco.CodeChallengeMethodsSupported.Any()) {
					throw new Exception($"The specified identity provider does not support PKCE");
				}

				if (!disco.CodeChallengeMethodsSupported.Contains(SHA256CodeChallengeMethod)) {
					throw new Exception($"The specified identity provider does not support the '{SHA256CodeChallengeMethod}' code challenge method");
				}

				_signingKeysUri = disco.JwksUri;
				_signingKeys = disco.KeySet.Keys.Select(jwk =>
					(AsymmetricSecurityKey)new RsaSecurityKey(
						new RSAParameters {
							Exponent = Base64Url.Decode(jwk.E),
							Modulus = Base64Url.Decode(jwk.N)
						}) {
						KeyId = jwk.Kid
					}).ToArray();
				_signingKeysLastRefresh = DateTime.UtcNow;

				var signingKeyIds = _signingKeys.Select(x => x.KeyId);
				_logger.Information("Issuer signing keys have been retrieved. Key IDs: {signingKeyIds}", signingKeyIds);

				_ready = true;
			} catch (Exception ex) {
				_logger.Fatal(ex, "Initialization failed.");
				throw;
			}
		}

		public override void Authenticate(AuthenticationRequest authenticationRequest) {
			if (!_ready) {
				authenticationRequest.NotReady();
				return;
			}

			try {
				var jwt = authenticationRequest.GetToken("jwt");

				if (jwt == null) {
					if (_options.LogFailedAuthenticationAttempts) {
						_logger.Warning("Authentication failed for {id}: {reason}",
							authenticationRequest.Id,
							"No token present.");
					}

					authenticationRequest.Unauthorized();
					return;
				}

				var jwtSecurityToken = _securityTokenHandler.ReadJwtToken(jwt);
				if (_signingKeys.All(x => x.KeyId != jwtSecurityToken.Header.Kid)) {
					lock(_signingKeysRefreshLock){
						if(DateTime.UtcNow - _signingKeysLastRefresh >= _signingKeysMinRefreshInterval)
						{
							try {
								//signing keys may have been rotated, refresh the keys before validation
								_logger.Verbose("An authentication request with an unknown key ID has been received. Refreshing issuer signing keys.");
								var oldKeyIds = _signingKeys.Select(x => x.KeyId).OrderBy(x => x);

								_signingKeys = _httpClient
									.GetJsonWebKeySetAsync(_signingKeysUri)
									.GetAwaiter()
									.GetResult()
									.KeySet
									.Keys.Select(jwk =>
										(AsymmetricSecurityKey)new RsaSecurityKey(
											new RSAParameters {
												Exponent = Base64Url.Decode(jwk.E),
												Modulus = Base64Url.Decode(jwk.N)
											}) {
											KeyId = jwk.Kid
										}).ToArray();

								var newKeyIds = _signingKeys.Select(x => x.KeyId).OrderBy(x => x);
								if (!oldKeyIds.SequenceEqual(newKeyIds)) {
									_logger.Information(
										"Issuer signing keys have changed. New Key IDs: {signingKeyIds}", newKeyIds);
								} else {
									_logger.Verbose(
										"Issuer signing keys have not changed. Key IDs: {signingKeyIds}", newKeyIds);
								}

								_signingKeysLastRefresh = DateTime.UtcNow;
							} catch (Exception ex) {
								_logger.Error(ex, "Failed to refresh issuer signing keys.");
							}
						} else {
							_logger.Verbose("An authentication request with an unknown key ID has been received. Skipping refresh of issuer signing keys since the minimum refresh interval of {minRefreshInterval} minutes has not yet expired.", _signingKeysMinRefreshInterval.TotalMinutes);
						}
					}
				}

				ClaimsPrincipal principal = _securityTokenHandler.ValidateToken(jwt, new TokenValidationParameters {
					ValidAudience = _options.Settings.Audience,
					ValidIssuer = _options.Settings.Issuer,
					ValidateAudience = true,
					ValidateIssuer = !_options.Settings.DisableIssuerValidation,
					ValidateLifetime = true,
					RequireExpirationTime = true,
					IssuerSigningKeys = _signingKeys
				}, out _);

				if (principal?.Identity == null) {
					authenticationRequest.Unauthorized();
					return;
				}

				if (!principal.Identity.IsAuthenticated) {
					if (_options.LogFailedAuthenticationAttempts) {
						_logger.Warning("Authentication failed for {id}: {reason}",
							authenticationRequest.Id,
							"Identity was not authenticated.");
					}

					authenticationRequest.Unauthorized();
					return;
				}

				authenticationRequest.Authenticated(principal);
			} catch (SecurityTokenValidationException ex) {
				if (_options.LogFailedAuthenticationAttempts) {
					_logger.Warning(ex, "Authentication failed for {id}: {reason}",
						authenticationRequest.Id,
						ex.Message);
				}

				authenticationRequest.Unauthorized();
			} catch (Exception ex) {
				if (_options.LogFailedAuthenticationAttempts) {
					_logger.Warning(ex, "Authentication failed for {id}: {reason}",
						authenticationRequest.Id,
						ex.Message);
				}

				authenticationRequest.Error();
			}
		}

		public override IEnumerable<KeyValuePair<string, string>> GetPublicProperties() {
			var result = new Dictionary<string, string> {
				{OidcConstants.Discovery.AuthorizationEndpoint, _authorizationEndpoint},
				{OidcConstants.AuthorizeRequest.ClientId, _options.Settings.ClientId},
				{OidcConstants.AuthorizeRequest.RedirectUri, CallBackUrl},
				{OidcConstants.AuthorizeRequest.ResponseType, AuthorizationCodeResponseType},
				{OidcConstants.AuthorizeRequest.Scope, OidcConstants.StandardScopes.OpenId + " " + OidcConstants.StandardScopes.Profile},
				{"code_challenge_uri", CodeChallengeUrl},
			};

			return result;
		}
		public override IReadOnlyList<string> GetSupportedAuthenticationSchemes() {
			return new[] {
				"Bearer"
			};
		}
		public override void ConfigureEndpoints(IEndpointRouteBuilder endpointRouteBuilder) {
			endpointRouteBuilder.Map(CallBackUrl, async context => {
				if (!_ready) {
					context.Response.StatusCode = (int) HttpStatusCode.ServiceUnavailable;
					return;
				}

				if (!context.Request.Query.TryGetValue(OidcConstants.AuthorizeResponse.State, out var state)) {
					if (_options.LogFailedAuthenticationAttempts) {
						_logger.Warning($"'{OidcConstants.AuthorizeResponse.State}' parameter was not provided in the OAuth callback URL.");
					}

					context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
					return;
				}

				var codeChallengeCorrelationId = string.Empty;
				try {
					var decodedBase64Bytes = Convert.FromBase64String(state!);
					var jsonString = Encoding.UTF8.GetString(decodedBase64Bytes);
					var dictionary = JsonConvert.DeserializeObject<Dictionary<string, string>>(jsonString);
					if (dictionary == null || !dictionary.TryGetValue("code_challenge_correlation_id", out codeChallengeCorrelationId)) {
						throw new ArgumentException("'code_challenge_correlation_id' is not present in the supplied JSON object");
					}
				} catch (Exception ex) {
					if (_options.LogFailedAuthenticationAttempts) {
						_logger.Warning(ex, $"Failed to parse the code challenge correlation ID from the '{OidcConstants.AuthorizeResponse.State}' parameter");
					}

					context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
					return;
				}

				if (!_codeVerifierLruCache.TryGetValue(codeChallengeCorrelationId, out var codeVerifier)) {
					if (_options.LogFailedAuthenticationAttempts) {
						_logger.Warning("Supplied code challenge correlation ID does not exist or may have expired.");
					}

					context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
					return;
				}

				_codeVerifierLruCache.Remove(codeChallengeCorrelationId);

				if (context.Request.Query.ContainsKey(OidcConstants.AuthorizeResponse.Error)) {
					var error = "Authorization Error: " + context.Request.Query[OidcConstants.AuthorizeResponse.Error];
					var errorMessage = context.Request.Query.ContainsKey(OidcConstants.AuthorizeResponse.ErrorDescription)
						? "Error Message: " + context.Request.Query[OidcConstants.AuthorizeResponse.ErrorDescription]
						: null;
					var stringBuilder = new StringBuilder();
					stringBuilder.Append("<html>");
					stringBuilder.Append("<head>");
					stringBuilder.Append("<meta http-equiv=\"refresh\" content=\"10;url=/\" />");
					stringBuilder.Append("</head>");
					stringBuilder.Append("<body>");
					stringBuilder.Append(error);
					stringBuilder.Append("<br>");
					if (errorMessage != null) {
						stringBuilder.Append(errorMessage);
						stringBuilder.Append("<br>");
					}
					stringBuilder.Append("</body>");
					stringBuilder.Append("</html>");

					var htmlOutput = stringBuilder.ToString();
					await context.Response.Body.WriteAsync(UTF8Encoding.UTF8.GetBytes(htmlOutput));
					return;
				}

				if (!context.Request.Query.ContainsKey(AuthorizationCodeResponseType)) {
					if (_options.LogFailedAuthenticationAttempts) {
						_logger.Warning($"'{AuthorizationCodeResponseType}' parameter was not provided in the OAuth callback URL.");
					}

					context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
					return;
				}

				try {
					var redirectUriBuilder = new UriBuilder {
						Scheme = context.Request.Scheme,
						Host = context.Request.Host.Host,
						Path = CallBackUrl
					};

					var port = context.Request.Host.Port;
					if (port.HasValue) {
						redirectUriBuilder.Port = port.Value;
					}

					var redirectUri = redirectUriBuilder.Uri.ToString();

					var queryParams = new Dictionary<string, string> {
						{OidcConstants.TokenRequest.GrantType, AuthorizationCodeGrantType},
						//{OidcConstants.TokenRequest.Code, context.Request.Query[AuthorizationCodeResponseType]},
						{OidcConstants.TokenRequest.RedirectUri, redirectUri},
						{OidcConstants.TokenRequest.ClientId, _options.Settings.ClientId},
						{OidcConstants.TokenRequest.CodeVerifier, codeVerifier}
					};

					if (context.Request.Query.TryGetValue(AuthorizationCodeResponseType, out var responseType)) {
						queryParams.Add(OidcConstants.TokenRequest.Code, responseType!);
					}

					//since we're using PKCE, client secrets can be optional depending on the configuration of the Identity Provider
					if (_options.Settings.ClientSecret != null) {
						queryParams.Add(OidcConstants.TokenRequest.ClientSecret, _options.Settings.ClientSecret);
					}

					var tokenUri = new UriBuilder(_tokenEndpoint).Uri;
					var result = await _httpClient.PostAsync(
						tokenUri,
						new FormUrlEncodedContent(queryParams.Select(
							x => new KeyValuePair<string, string>(x.Key, x.Value))
						)
					);

					if (result.StatusCode != HttpStatusCode.OK) {
						context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
						if (_options.LogFailedAuthenticationAttempts) {
							var content = await result.Content.ReadAsStringAsync();
							_logger.Warning("Failed to retrieve access token for client ID: {clientId}. HTTP Status code: {httpStatusCode}, content: {content}", _options.Settings.ClientId, result.StatusCode, content);
						}
						return;
					}

					var resultString = await result.Content.ReadAsStringAsync();
					var resultDictionary = JsonConvert.DeserializeObject<Dictionary<string, string>>(resultString) ?? new();

					const string idToken = OidcConstants.TokenResponse.IdentityToken;
					const string tokenType = OidcConstants.TokenResponse.TokenType;
					const string bearerTokenType = OidcConstants.TokenResponse.BearerTokenType;

					if (!resultDictionary.ContainsKey(idToken)) {
						throw new Exception($"'{idToken}' is not present in token response.");
					}

					if (!resultDictionary.ContainsKey(tokenType)) {
						throw new Exception($"'{tokenType}' is not present in token response.");
					}

					if (resultDictionary[tokenType] != bearerTokenType) {
						throw new Exception($"'{tokenType}' is not '{bearerTokenType}' in token response. Value is : {resultDictionary[tokenType]}");
					}

					var cookieManager = context.Response.Cookies;
					var cookieOptions = new CookieOptions {
						MaxAge = TimeSpan.FromMinutes(1)
					};

					cookieManager.Append("oauth_id_token", resultDictionary[idToken], cookieOptions);
					context.Response.Redirect("/web");
				} catch (Exception ex) {
					context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
					_logger.Error(ex, " Error retrieving access token from {tokenEndpoint}", _tokenEndpoint);
				}
			});

			endpointRouteBuilder.Map(CodeChallengeUrl, async context => {
				if (!_ready) {
					context.Response.StatusCode = (int) HttpStatusCode.ServiceUnavailable;
					return;
				}

				var randomBytes = new byte[32];
				_rngCsp.GetBytes(randomBytes);

				var codeVerifier = Rfc7636Base64Encode(randomBytes);
				var codeVerifierAsciiBytes = Encoding.ASCII.GetBytes(codeVerifier);

				var codeChallenge = string.Empty;
				using (SHA256 sha256Hash = SHA256.Create())
				{
					var sha256HashBytes = sha256Hash.ComputeHash(codeVerifierAsciiBytes);
					codeChallenge = Rfc7636Base64Encode(sha256HashBytes);
				}

				var codeChallengeCorrelationId = Guid.NewGuid().ToString();
				_codeVerifierLruCache.Add(codeChallengeCorrelationId, codeVerifier);

				var result = new Dictionary<string, string> {
					{"code_challenge", codeChallenge},
					{"code_challenge_method", SHA256CodeChallengeMethod},
					{"code_challenge_correlation_id", codeChallengeCorrelationId}
				};

				context.Response.ContentType = "application/json";
				await context.Response.Body.WriteAsync(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(result)));
			});
		}

		private string Rfc7636Base64Encode(byte[] bytes) {
			var s = Convert.ToBase64String(bytes);
			s = s.Split('=')[0]; // Remove any trailing '='s
			s = s.Replace('+', '-'); // 62nd char of encoding
			s = s.Replace('/', '_'); // 63rd char of encoding
			return s;
		}
	}

	private class Options {
		public Settings Settings { get; set; } = new Settings();
		public bool LogFailedAuthenticationAttempts { get; set; }
	}

	private class Settings {
		public string Audience { get; set; } = null!;
		public string Issuer { get; set; } = null!;

		public bool DisableIssuerValidation { get; set; } = false;
		public string Idp { get; set; } = null!;
		public bool Insecure { get; set; }
		public Uri IdpUri => Uri.IsWellFormedUriString(Idp, UriKind.Absolute)
			? new Uri(Idp)
			: new Uri(Issuer);
		public string ClientId { get; set; } = null!;
		public string ClientSecret { get; set; } = null!;
		public string[] AdditionalEndpointBaseAddresses { get; set; } = { };
	}
}
