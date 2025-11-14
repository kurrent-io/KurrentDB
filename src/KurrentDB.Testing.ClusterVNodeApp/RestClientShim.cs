// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net;
using Polly;
using Polly.Retry;
using RestSharp;
using RestSharp.Authenticators;
using TUnit.Core.Interfaces;

namespace KurrentDB.Testing;

public sealed class RestClientShim : IAsyncInitializer, IDisposable {
	[ClassDataSource<NodeShim>(Shared = SharedType.PerTestSession)]
	public required NodeShim NodeShim { get; init; }

	public IRestClient Client { get; private set; } = null!;

	public async Task InitializeAsync() {
		var username = NodeShim.NodeShimOptions.Username;
		var password = NodeShim.NodeShimOptions.Password;

		Client = new RestClient(
			new RestClientOptions() {
				Authenticator = new HttpBasicAuthenticator(
					username: username,
					password: password),
				BaseUrl = NodeShim.Node.Uri,
				RemoteCertificateValidationCallback = (_, _, _, _) => true,
				ThrowOnAnyError = true,
			}).AddDefaultHeaders(new() {
				{ "Content-Type", "application/json"},
			});

		await new ResiliencePipelineBuilder()
			.AddRetry(new RetryStrategyOptions() {
				Delay = TimeSpan.FromMilliseconds(1000),
				MaxRetryAttempts = 5,
			})
			.Build()
			.ExecuteAsync(async ct => {
				var response = await Client.GetAsync(new RestRequest($"health/live"), ct);
				if (response.StatusCode != HttpStatusCode.NoContent)
					throw new Exception("Not ready yet");
			});
	}

	public void Dispose() {
		Client?.Dispose();
	}
}
