// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using EventStore.Licensing.Keygen;
using RestSharp;
using Xunit;

namespace EventStore.Licensing.Tests.Keygen;

public sealed class KeygenLifecycleServiceTests : IDisposable {
	readonly Channel<LicenseInfo> _licenses;
	readonly KeygenSimulator _keygen;
	readonly KeygenLifecycleService _sut;

	public KeygenLifecycleServiceTests() {
		_licenses = Channel.CreateUnbounded<LicenseInfo>();
		var options = new KeygenClientOptions {
			Licensing = new() {
				LicenseKey = "the-key",
			},
			ReadOnlyReplica = true,
			Archiver = true,
		};
		_keygen = new KeygenSimulator();
		_sut = new KeygenLifecycleService(
			new KeygenClient(
				options,
				new RestClient(
					new RestClientOptions($"https://mock-key-gen") {
						ConfigureMessageHandler = _ => _keygen,
					})),
			new Fingerprint(port: null),
			revalidationDelay: TimeSpan.FromMilliseconds(10));

		_sut.Licenses.Subscribe(async x => await _licenses.Writer.WriteAsync(x));
	}

	public void Dispose() {
		_sut.Dispose();
	}

	async Task AssertNextLicense(LicenseInfo expected) {
		var actual = await _licenses.Reader.ReadAsync();

		if (expected is LicenseInfo.Conclusive expectedC) {
			var actualC = Assert.IsType<LicenseInfo.Conclusive>(actual);
			Assert.Equal(expectedC.LicenseId, actualC.LicenseId);
			Assert.Equal(expectedC.Name, actualC.Name);
			Assert.Equal(expectedC.Valid, actualC.Valid);
			Assert.Equal(expectedC.Trial, actualC.Trial);
			Assert.Equal(expectedC.Warning, actualC.Warning);
			Assert.Equal(expectedC.Detail, actualC.Detail);
			Assert.Equal(expectedC.Expiry, actualC.Expiry);
			Assert.Equal(expectedC.Entitlements, actualC.Entitlements);

		} else if (expected is LicenseInfo.Inconclusive) {
			Assert.IsType<LicenseInfo.Inconclusive>(actual);

		} else if (expected is LicenseInfo.RetryImmediately) {
			Assert.IsType<LicenseInfo.RetryImmediately>(actual);
		} else {
			throw new ArgumentException("unexpected type");
		}
	}

	[Fact]
	public async Task when_license_validation_succeeds() {
		await _sut.StartAsync(CancellationToken.None);

		await _keygen.ShouldReceive_ValidationRequest();
		await _keygen.ReplyWith_ValidationResponse("VALID");

		await _keygen.ShouldReceive_EntitlementRequest();
		await _keygen.ReplyWith_Entitlements("A_SPECIAL_ENTITLEMENT");

		await AssertNextLicense(new LicenseInfo.Conclusive(
			LicenseId: "the-license-id",
			Name: "the name of the license",
			Valid: true,
			Trial: false,
			Warning: false,
			Detail: "valid",
			Expiry: null,
			Entitlements: ["A_SPECIAL_ENTITLEMENT"]));

		await _keygen.ShouldReceive_GetMachine();
		await _keygen.ReplyWith_Machine();

		await _keygen.ShouldReceive_Heartbeat();
		await _keygen.ReplyWith_HeartbeatResponse();

		await _keygen.ShouldReceive_Heartbeat();
		await _keygen.ReplyWith_HeartbeatResponse();
	}

	[Theory]
	[InlineData("NO_MACHINES")]
	[InlineData("FINGERPRINT_SCOPE_MISMATCH")]
	public async Task when_license_validation_requires_machine_activation_which_succeeds(string code) {
		await _sut.StartAsync(CancellationToken.None);

		await _keygen.ShouldReceive_ValidationRequest();
		await _keygen.ReplyWith_ValidationResponse(code);

		await _keygen.ShouldReceive_ActivationRequest();
		await _keygen.ReplyWith_ActivationSuccess();

		await _keygen.ShouldReceive_ValidationRequest();
	}

	// when activating a machine, FINGERPRINT_TAKEN can be returned for two reasons
	// https://github.com/keygen-sh/keygen-api/blob/master/features/api/v1/machines/create.feature
	// 1. "Scenario: License creates a machine for their license with a duplicate fingerprint"
	//      this can happen for us if two nodes using the same license on the same machine try to
	//      activate it at the same time.
	//      this is a temporary error which should be resolved by revalidating the license.
	// 2. "Scenario: License creates a machine with a fingerprint matching another license's machine for a
	//      policy-scoped machine uniqueness strategy (same policy)"
	//      this can happen for us if we try to activate a machine for a trial license while the same machine
	//      is active for a another trial license.
	//      this is a conclusive error that should result in no license.
	[Theory]
	[InlineData("FINGERPRINT_TAKEN", "has already been taken", false)]
	[InlineData("FINGERPRINT_TAKEN", "has already been taken for this policy", true)]
	[InlineData("MACHINE_LIMIT_EXCEEDED", "machine limit exceeded", true)]
	[InlineData("MACHINE_CORE_LIMIT_EXCEEDED", "core limit exceeded", true)]
	[InlineData("something_we_didnt_anticipate", "another", false)]
	public async Task when_license_validation_requires_machine_activation_which_fails(string code, string detail, bool conclusive) {
		await _sut.StartAsync(CancellationToken.None);

		await _keygen.ShouldReceive_ValidationRequest();
		await _keygen.ReplyWith_ValidationResponse("NO_MACHINES");

		await _keygen.ShouldReceive_ActivationRequest();
		await _keygen.ReplyWith_ActivationError(code, detail);

		if (conclusive) {
			await AssertNextLicense(new LicenseInfo.Conclusive(
				LicenseId: "Unknown",
				Name: "Unknown",
				Valid: false,
				Trial: false,
				Warning: true,
				Detail: $"{code.ToLower()}. {detail}.",
				Expiry: null,
				Entitlements: []));
		} else {
			await AssertNextLicense(new LicenseInfo.Inconclusive());
			await _keygen.ShouldReceive_ValidationRequest();
		}
	}

	[Theory]
	[InlineData("HEARTBEAT_NOT_STARTED")]
	[InlineData("HEARTBEAT_DEAD")]
	public async Task when_license_validation_requires_machine_deactivation_which_succeeds(string code) {
		await _sut.StartAsync(CancellationToken.None);

		await _keygen.ShouldReceive_ValidationRequest();
		await _keygen.ReplyWith_ValidationResponse(code);

		await _keygen.ShouldReceive_DeactivationRequest();
		await _keygen.ReplyWith_DeactivationSuccess();

		await _keygen.ShouldReceive_ValidationRequest();
	}

	[Fact]
	public async Task when_license_validation_requires_machine_deactivation_which_fails() {
		await _sut.StartAsync(CancellationToken.None);

		await _keygen.ShouldReceive_ValidationRequest();
		await _keygen.ReplyWith_ValidationResponse("HEARTBEAT_DEAD");

		await _keygen.ShouldReceive_DeactivationRequest();
		await _keygen.ReplyWith_DeactivationError();

		await AssertNextLicense(new LicenseInfo.Inconclusive());
		await _keygen.ShouldReceive_ValidationRequest();
	}

	[Fact]
	public async Task when_license_validation_was_unanticipated() {
		await _sut.StartAsync(CancellationToken.None);

		await _keygen.ShouldReceive_ValidationRequest();
		await _keygen.ReplyWith_ValidationResponse("something_we_didnt_anticipate");

		await AssertNextLicense(new LicenseInfo.Conclusive(
			LicenseId: "the-license-id",
			Name: "the name of the license",
			Valid: false,
			Trial: false,
			Warning: true,
			Detail: "something_we_didnt_anticipate",
			Expiry: null,
			Entitlements: []));
	}

	[Theory]
	[InlineData("LICENSE_INVALID", true)]
	[InlineData("LICENSE_SUSPENDED", true)]
	[InlineData("LICENSE_EXPIRED", true)]
	[InlineData("something_we_didnt_anticipate", false)]
	public async Task when_license_validation_fails(string code, bool conclusive) {
		await _sut.StartAsync(CancellationToken.None);

		await _keygen.ShouldReceive_ValidationRequest();
		await _keygen.ReplyWith_Error(code);

		if (conclusive) {
			await AssertNextLicense(new LicenseInfo.Conclusive(
				LicenseId: "Unknown",
				Name: "Unknown",
				Valid: false,
				Trial: false,
				Warning: true,
				Detail: code.ToLower(),
				Expiry: null,
				Entitlements: []));
		} else {
			await AssertNextLicense(new LicenseInfo.Inconclusive());
			await _keygen.ShouldReceive_ValidationRequest();
		}
	}

	[Fact]
	public async Task when_license_becomes_suspended() {
		await when_license_validation_succeeds();

		await _keygen.ShouldReceive_Heartbeat();
		await _keygen.ReplyWith_Error("LICENSE_SUSPENDED");

		await _keygen.ShouldReceive_ValidationRequest();
	}
}
