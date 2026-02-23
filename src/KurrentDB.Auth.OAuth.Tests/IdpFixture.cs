// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Ductus.FluentDocker.Builders;
using Ductus.FluentDocker.Model.Builders;
using Ductus.FluentDocker.Services;
using IdentityModel.Client;
using Polly;
using Xunit.Abstractions;

namespace KurrentDB.Auth.OAuth.Tests;

internal class IdpFixture : IDisposable {
	private const int IdpPort = 5001;
	public const string ClientId = "kurrentdb-client";
	public const string ClientSecret = "secret";

	private static string SourceDirectory =>
		Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "../../../../src"));

	private static string TestSourceDirectory => Path.Combine(SourceDirectory, "KurrentDB.Auth.OAuth.Tests");

	private static string IdentityServerSourceDirectory => Path.Combine(SourceDirectory, "IdentityServer");

	private static string IdentityServerConfigurationFile =>
		Path.Combine(Environment.CurrentDirectory, "conf", "idsrv4.conf.json");

	private static string IdentityServerUserConfigurationFile =>
		Path.Combine(Environment.CurrentDirectory, "conf", "users.conf.json");

	private readonly ITestOutputHelper _output;
	private readonly IContainerService _identityServer;
	private readonly TaskCompletionSource<DiscoveryDocumentResponse> _discoveryDocumentSource;
	public HttpClient HttpClient { get; }

	public IdpFixture(ITestOutputHelper output) {
		_output = output;

		_identityServer = new Builder()
			.UseContainer()
			.UseImage("ghcr.io/kurrent-io/idsrv4/idsrv4")
			.WithName("es-oauth-tests-idsrv4")
			.Mount(IdentityServerConfigurationFile, "/etc/idsrv4/idsrv4.conf", MountType.ReadOnly)
			.Mount(IdentityServerUserConfigurationFile, "/etc/idsrv4/users.conf", MountType.ReadOnly)
			.ExposePort(IdpPort, IdpPort)
			.Build();

		_identityServer.StopOnDispose = true;
		_discoveryDocumentSource = new TaskCompletionSource<DiscoveryDocumentResponse>();

		HttpClient = new HttpClient(new SocketsHttpHandler {
			SslOptions = {
				RemoteCertificateValidationCallback = delegate { return true; }
			}
		}, true) {
			BaseAddress = new UriBuilder {
				Scheme = Uri.UriSchemeHttps,
				Port = IdpPort
			}.Uri
		};
	}

	public async Task Start() {
		_identityServer.ShipContainerLogs(_output);
		_identityServer.Start();

		try {
			await Policy.Handle<Exception>()
				.WaitAndRetryAsync(5, retryCount => TimeSpan.FromSeconds(retryCount * retryCount))
				.ExecuteAsync(async () => {
					var discoveryDocument = await HttpClient.GetDiscoveryDocumentAsync();
					if (discoveryDocument?.HttpResponse == null) {
						throw new Exception("Health check not available yet");
					}
					if (discoveryDocument.HttpStatusCode != HttpStatusCode.OK) {
						throw new Exception($"Health check failed with status code {discoveryDocument.HttpStatusCode}");
					}
					_discoveryDocumentSource.SetResult(discoveryDocument);
				});
		} catch (Exception) {
			_identityServer.Dispose();
			throw;
		}
	}


	public async Task<string> GetAccessToken(string username, string password) {
		var response = await GetTokenResponse(username, password);

		return response.AccessToken;
	}

	public async Task<string> GetRefreshToken(string username, string password) {
		var response = await GetTokenResponse(username, password);

		return response.RefreshToken;
	}

	private async Task<TokenResponse> GetTokenResponse(string username, string password) {
		var discoveryDocument = await _discoveryDocumentSource.Task;
		var response = await HttpClient.RequestPasswordTokenAsync(new PasswordTokenRequest {
			ClientId = ClientId,
			ClientSecret = ClientSecret,
			UserName = username,
			Password = password,
			Address = discoveryDocument.TokenEndpoint,
			Scope = "openid streams offline_access"
		});

		if (response.Exception != null) {
			throw response.Exception;
		}

		return response;
	}

	public string Issuer {
		get {
			var config = _identityServer.GetConfiguration();
			return $"https://{config.NetworkSettings.Networks["bridge"].IPAddress}:{IdpPort}";
		}
	}

	public void Dispose() {
		_identityServer?.Dispose();
	}
}
