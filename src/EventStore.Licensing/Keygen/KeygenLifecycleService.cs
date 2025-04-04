// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics.CodeAnalysis;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Microsoft.Extensions.Hosting;
using RestSharp;
using Serilog;
using static EventStore.Licensing.Keygen.Models;

namespace EventStore.Licensing.Keygen;

// Uses a client to try to create and maintain a license activation for this machine fingerprint.
// current status is available via the Licenses observable.
// responsible for navigating the keygen lifecycle
// not responsible for business decisions
public sealed class KeygenLifecycleService : IHostedService, IDisposable {
	private static readonly ILogger Log = Serilog.Log.ForContext<KeygenLifecycleService>();

	readonly KeygenClient _client;
	readonly string _fingerprint;
	readonly TimeSpan _revalidationDelay;
	readonly CancellationTokenSource _heartbeatCancellation = new();
	readonly ReplaySubject<LicenseInfo> _licenses = new(bufferSize: 1);

	public KeygenLifecycleService(KeygenClient client, Fingerprint fingerprint, TimeSpan revalidationDelay) {
		_client = client;
		_fingerprint = fingerprint.Get();
		_revalidationDelay = revalidationDelay;
	}

	public IObservable<LicenseInfo> Licenses => _licenses;

	public Task StartAsync(CancellationToken cancellationToken) {
		Log.Information("Resources: {CpuCount} CPU, {Ram} MB RAM", Fingerprint.CpuCount, Fingerprint.Ram / 1024 / 1024);

		_ = MainLoop(_heartbeatCancellation.Token);
		return Task.CompletedTask;
	}

	public async Task StopAsync(CancellationToken cancellationToken) {
		await _heartbeatCancellation.CancelAsync();
	}

	public void Dispose() {
		_heartbeatCancellation.Cancel();
		_heartbeatCancellation.Dispose();
		_client.Dispose();
	}

	async Task MainLoop(CancellationToken cancellationToken) {
		while (true) {
			var info = await Validate(cancellationToken);
			_licenses.OnNext(info);

			switch (info) {
				case LicenseInfo.RetryImmediately:
					Log.Debug("License validation requested immediate retry for activation/deactivation");
					await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
					break;

				case LicenseInfo.Inconclusive:
					Log.Warning("License validation was inconclusive");
					await Task.Delay(_revalidationDelay, cancellationToken);
					break;

				case LicenseInfo.Conclusive conclusion:
					Log.Information("License validation was conclusive");

					if (!conclusion.Valid) {
						// license is conclusively not valid. nothing else to do.
						return;
					}

					await HeartbeatAsNecessary(cancellationToken);

					// heartbeat process has failed, start over.
					await Task.Delay(_revalidationDelay, cancellationToken);
					break;
			}
		}
	}

	async Task<LicenseInfo> Validate(CancellationToken cancellationToken) {
		var restResponse = await _client.ValidateLicense(_fingerprint, cancellationToken);
		if (!TryGetParsedResponse(restResponse, "License validation", out var response, out var error)) {
			return error.ToLicenseInfo();
		}

		var validationData = response.Meta;
		var licenseData = response.Data;
		var licenseAttributes = response.Data?.Attributes;

		if (licenseData == null || validationData == null || licenseAttributes == null) {
			Log.Warning("License validation missing expected data");
			return LicenseInfo.Inconclusive.Instance;
		}

		var licenseName = licenseAttributes.Name;
		switch (response.GetStatus()) {
			case LicenseStatus.InvalidNoMachines:
			case LicenseStatus.InvalidMachineMismatch:
				return await Activate();

			case LicenseStatus.InvalidHeartbeatNotStarted:
			case LicenseStatus.InvalidHeartbeatDead:
				return await Deactivate();

			case LicenseStatus.InvalidOther:
				return CreateResult([]);
		}

		// status is Valid
		var entitlementRestResponse = await _client.GetEntitlements(licenseData.Id);
		if (!TryGetParsedResponse(entitlementRestResponse, "License GetEntitlements", out var entitlementResponse, out var entitlementError)) {
			return entitlementError.ToLicenseInfo();
		}

		var entitlements = entitlementResponse.Data!.Select(x => x.Attributes).ToArray();
		foreach (var entitlement in entitlements)
			Log.Information("Available license entitlement: {Entitlement}", entitlement.Name);

		return CreateResult(entitlements);

		LicenseInfo CreateResult(EntitlementAttributes[] entitlements) =>
			new LicenseInfo.Conclusive(
				LicenseId: licenseData.Id,
				Name: licenseName,
				Valid: validationData.Valid,
				Trial: GetMetaBool("trial"),
				Warning: validationData.Code != "VALID",
				Detail: validationData.Detail,
				Expiry: licenseAttributes.Expiry,
				Entitlements: entitlements.Select(x => x.Code).ToArray());

		bool GetMetaBool(string name) =>
			licenseAttributes.Metadata.TryGetValue(name, out var value) &&
			bool.Parse(value.ToString()?.ToLowerInvariant() ?? "false");

		async Task<LicenseInfo> Activate() {
			var restResponse = await _client.ActivateMachine(licenseData.Id, _fingerprint, Fingerprint.CpuCount, Fingerprint.Ram, cancellationToken);

			if (!TryGetParsedResponse(restResponse, "License machine activation", out var response, out var error)) {
				return error.ToLicenseInfo();
			}

			Log.Information("Machine activated");
			return LicenseInfo.RetryImmediately.Instance;
		}

		async Task<LicenseInfo> Deactivate() {
			var restResponse = await _client.DeactivateMachine(_fingerprint, cancellationToken);

			if (restResponse.StatusCode != System.Net.HttpStatusCode.NoContent) {
				Log.Information("License machine deactivation failed");
				return LicenseInfo.Inconclusive.Instance;
			}

			Log.Information("Machine deactivated");
			return LicenseInfo.RetryImmediately.Instance;
		}
	}

	// completes when the heartbeat fails
	async Task HeartbeatAsNecessary(CancellationToken cancellationToken) {
		var restResponse = await _client.GetMachine(_fingerprint, cancellationToken);
		if (!TryGetParsedResponse(restResponse, "License GetMachine", out var response, out var _)) {
			// doesn't matter what the error is, we will revalidate from the top.
			return;
		}

		if (!response.RequiresHeartbeat) {
			Log.Debug("No heartbeat required");
			await Task.Delay(Timeout.Infinite, cancellationToken);
		}

		await MaintainHeartbeat(response.HeartbeatInterval, cancellationToken);
	}

	// completes when the heartbeat fails
	async Task MaintainHeartbeat(int interval, CancellationToken cancellationToken) {
		Log.Debug("Starting heartbeat with interval {Interval} seconds", interval);

		var delay = TimeSpan.FromSeconds(interval > 10
			? interval - 10
			: interval / 5.0);

		while (true) {
			await Task.Delay(delay, cancellationToken);

			var restResponse = await _client.SendHeartbeat(_fingerprint, cancellationToken);
			if (!TryGetParsedResponse(restResponse, "License Heartbeat", out var response, out var _)) {
				// doesn't matter what the error is, we will revalidate from the top.
				return;
			}

			Log.Debug("Heartbeat Status: {Status}", response.HeartbeatStatus);

			if (response.HeartbeatStatus != "ALIVE") {
				return;
			}
		}
	}

	static bool TryGetParsedResponse<T>(
		RestResponse<T> restResponse,
		string operation,
		[MaybeNullWhen(false)] out T response,
		[MaybeNullWhen(true)] out KeygenError error)
		where T : KeygenResponse {

		if (restResponse.Data is not { } data) {
			Log.Information($"{operation} returned no response. HttpStatus {{Status}}. ResponseStatus: {{ResponseStatus}}. Error: {{Error}}",
				restResponse.StatusCode,
				restResponse.ResponseStatus,
				restResponse.ErrorMessage);
			response = default;
			error = new() {
				Title = "No data",
				Code = "ESDB_NO_DATA",
				Detail = $"Received no data that we could parse",
			};
			return false;
		}

		response = data;

		if (response.IsSuccess) {
			error = default;
			return true;
		}

		if (response.Errors.Length != 0) {
			Log.Information($"{operation} (status {{Status}}) returned an error: {{Error}}",
				restResponse.StatusCode, response.Errors[0]);
			error = response.Errors[0];
		} else {
			Log.Information($"{operation} (status {{Status}}) failed with no error", restResponse.StatusCode);
			error = new() {
				Title = "No error",
				Code = "ESDB_NO_ERROR",
				Detail = "Received no data but no error either",
			};
		}

		return false;
	}
}

static class ErrorExtensions {
	static readonly string[] ConclusiveErrors = [
		// Policy determines the scope of a Machine Fingerprint (unique per Account/Policy/License)
		"FINGERPRINT_TAKEN",
		"LICENSE_EXPIRED",
		"LICENSE_INVALID",
		"LICENSE_SUSPENDED",
		"MACHINE_CORE_LIMIT_EXCEEDED",
		"MACHINE_LIMIT_EXCEEDED",
		"MACHINE_PROCESS_LIMIT_EXCEEDED",
	];

	public static LicenseInfo ToLicenseInfo(this KeygenError error) {
		// FINGERPRINT_TAKEN is special, see when_license_validation_requires_machine_activation_which_fails
		if (error.Code == "FINGERPRINT_TAKEN" && !error.Detail.Contains("policy"))
			return LicenseInfo.Inconclusive.Instance;

		if (!ConclusiveErrors.Contains(error.Code))
			return LicenseInfo.Inconclusive.Instance;

		var errorString = error.Title;
		if (!string.IsNullOrWhiteSpace(error.Detail))
			errorString = $"{errorString}. {error.Detail}.";
		return LicenseInfo.Conclusive.FromError(errorString);
	}
}
