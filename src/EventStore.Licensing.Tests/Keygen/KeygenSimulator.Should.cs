// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using EventStore.Licensing.Keygen;
using Xunit;

namespace EventStore.Licensing.Tests.Keygen;

partial class KeygenSimulator {
	public async Task<Models.ValidateLicenseRequest> ShouldReceive_ValidationRequest() {
		var request = await Receive();
		Assert.Equal("https://mock-key-gen/licenses/actions/validate-key", $"{request.RequestUri}");
		Assert.Equal(HttpMethod.Post, request.Method);

		var s = await request.Content!.ReadAsStringAsync();
		var r = JsonSerializer.Deserialize<Models.ValidateLicenseRequest>(s, _serializerOptions)
			?? throw new System.Exception("Could not deserialize request");

		Assert.Equal("the-key", r.Meta.Key);
		Assert.Equal(_fingerprint, r.Meta.Scope.Fingerprint);
		return r;
	}

	public async Task ShouldReceive_EntitlementRequest() {
		var request = await Receive();
		Assert.Equal("https://mock-key-gen/licenses/the-license-id/entitlements?limit=100", $"{request.RequestUri}");
		Assert.Equal(HttpMethod.Get, request.Method);
	}

	public async Task ShouldReceive_GetMachine() {
		var request = await Receive();
		Assert.Equal($"https://mock-key-gen/machines/{_fingerprint}", $"{request.RequestUri}");
		Assert.Equal(HttpMethod.Get, request.Method);
	}

	public async Task ShouldReceive_Heartbeat() {
		var request = await Receive();
		Assert.Equal($"https://mock-key-gen/machines/{_fingerprint}/actions/ping", $"{request.RequestUri}");
		Assert.Equal(HttpMethod.Post, request.Method);
	}

	public async Task ShouldReceive_ActivationRequest() {
		var request = await Receive();
		Assert.Equal($"https://mock-key-gen/machines", $"{request.RequestUri}");
		Assert.Equal(HttpMethod.Post, request.Method);

		var s = await request.Content!.ReadAsStringAsync();
		var r = JsonNode.Parse(s);

		var data = r!["data"];
		Assert.Equal("machines", $"{data!["type"]}");

		var attributes = data!["attributes"]!;
		Assert.Equal(_fingerprint, $"{attributes["fingerprint"]}");
		Assert.Equal(RuntimeInformation.OSDescription, $"{attributes["platform"]}");
		Assert.Equal(Dns.GetHostName(), $"{attributes["name"]}");
		Assert.Equal(Dns.GetHostName(), $"{attributes["hostname"]}");
		Assert.Equal($"{Fingerprint.CpuCount}", $"{attributes["cores"]}");
		Assert.Equal($"{Fingerprint.Ram}", $"{attributes["metadata"]!["ram"]}");
		Assert.Equal($"true", $"{attributes["metadata"]!["readOnlyReplica"]}");
		Assert.Equal($"true", $"{attributes["metadata"]!["archiver"]}");

		var relationships = data!["relationships"]!["license"]!["data"];
		Assert.Equal("licenses", $"{relationships!["type"]}");
		Assert.Equal("the-license-id", $"{relationships!["id"]}");
	}

	public async Task ShouldReceive_DeactivationRequest() {
		var request = await Receive();
		Assert.Equal($"https://mock-key-gen/machines/{_fingerprint}", $"{request.RequestUri}");
		Assert.Equal(HttpMethod.Delete, request.Method);
	}
}
