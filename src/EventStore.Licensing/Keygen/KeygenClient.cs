// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net;
using System.Runtime.InteropServices;
using System.Text.Json;
using RestSharp;
using Serilog;
using static EventStore.Licensing.Keygen.Models;

namespace EventStore.Licensing.Keygen;

// REST client
// responsible for
// - authenticating ourselves with keygen
// - authenticating the responses
// - (de)serializing requests/responses
// not responsible for interpreting the semantics of the reponses or business decisions
// does not throw exceptions
public sealed class KeygenClient : IDisposable {
	const string ContentType = "application/vnd.api+json";

	static readonly ILogger Log = Serilog.Log.ForContext<KeygenClient>();

	static readonly JsonSerializerOptions JsonSerializerOptions = new() {
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	};

	readonly KeygenClientOptions _options;
	readonly RestClient _client;

	public KeygenClient(KeygenClientOptions options, RestClient restClient) {
		_options = options;
		_client = restClient;
		_client.AcceptedContentTypes = [ContentType];
		_client.AddDefaultHeader("Keygen-Accept-Signature", "algorithm=\"rsa-sha256\"");
	}

	public void Dispose() => _client.Dispose();

	// Validates that this machine is licensed with this license.
	// https://keygen.sh/docs/api/licenses/#licenses-actions-validate-key
	public async Task<RestResponse<ValidateLicenseResponse>> ValidateLicense(string fingerprint, CancellationToken cancellationToken) {
		Log.Information("Validating license with fingerprint \"{Fingerprint}\"", fingerprint);
		var request = new RestRequest("licenses/actions/validate-key")
			.AddJsonBody(new ValidateLicenseRequest(
				Meta: new(
					Key: _options.Licensing.LicenseKey,
					Scope: new(Fingerprint: fingerprint))));

		var response = await _client.ExecutePostAsync<ValidateLicenseResponse>(request, cancellationToken);
		return response;
	}

	// https://keygen.sh/docs/api/machines/#machines-create
	public async Task<RestResponse<ActivateMachineResponse>> ActivateMachine(string licenseId, string fingerprint, int cpu, long ram, CancellationToken cancellationToken) {
		Log.Information("Activating machine");
		var machineName = Dns.GetHostName();
		var request = new RestRequest("machines", Method.Post)
			.AddJsonBody(
				new {
					data = new {
						type = "machines",
						attributes = new {
							fingerprint = fingerprint,
							platform = RuntimeInformation.OSDescription,
							name = machineName,
							hostname = machineName,
							cores = cpu,
							metadata = new Dictionary<string, string> {
								["ram"] = ram.ToString(),
								["readOnlyReplica"] = _options.ReadOnlyReplica.ToString().ToLower(),
								["archiver"] = _options.Archiver.ToString().ToLower(),
							},
						},
						relationships = new {
							license = new {
								data = new {
									type = "licenses",
									id = licenseId
								},
							},
						},
					},
				},
				ContentType);

		var response = await _client.ExecuteAsync<ActivateMachineResponse>(request, cancellationToken);
		return response;
	}

	// https://keygen.sh/docs/api/machines/#machines-delete
	public async Task<RestResponse> DeactivateMachine(string fingerprint, CancellationToken cancellationToken) {
		Log.Information("Deactivating machine");
		var request = new RestRequest($"machines/{fingerprint}");
		var response = await _client.DeleteAsync(request, cancellationToken);
		return response;
	}

	// https://keygen.sh/docs/api/machines/#machines-retrieve
	public async Task<RestResponse<GetMachineResponse>> GetMachine(string fingerprint, CancellationToken cancellationToken) {
		var request = new RestRequest($"machines/{fingerprint}");
		var response = await _client.ExecuteAsync<GetMachineResponse>(request, cancellationToken);
		return response;
	}

	// https://keygen.sh/docs/api/machines/#machines-actions-ping
	public async Task<RestResponse<HeartbeatResponse>> SendHeartbeat(string fingerprint, CancellationToken cancellationToken) {
		Log.Debug("Sending heartbeat");
		var request = new RestRequest($"machines/{fingerprint}/actions/ping", Method.Post);
		var response = await _client.ExecuteAsync<HeartbeatResponse>(request, cancellationToken);
		return response;
	}

	public async Task<RestResponse<EntitlementsResponse>> GetEntitlements(string licenseId) {
		var request = new RestRequest($"licenses/{licenseId}/entitlements");
		request.AddQueryParameter("limit", 100); // only gets 10 entitlements by default
		var response = await _client.ExecuteGetAsync<EntitlementsResponse>(request);
		return response;
	}
}
