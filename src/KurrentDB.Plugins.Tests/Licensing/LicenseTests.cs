// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Security.Cryptography;
using EventStore.Plugins.Licensing;
using License = EventStore.Plugins.Licensing.License;

namespace EventStore.Plugins.Tests.Licensing;

public class LicenseTests {
	static (string PublicKey, string PrivateKey) CreateKeyPair() {
		using var rsa = RSA.Create(LicenseConstants.RsaMinimumKeySizeInBits);
		var publicKey = Convert.ToBase64String(rsa.ExportRSAPublicKey());
		var privateKey = Convert.ToBase64String(rsa.ExportRSAPrivateKey());
		return (publicKey, privateKey);
	}

	[Fact]
	public async Task can_create_and_validate_license() {
		var license = await License.CreateAsync(new Dictionary<string, object> {
			{ "foo", "bar" },
			{ "my_entitlement", "true" },
		});

		// check repeatedly because of https://github.com/dotnet/runtime/issues/43087
		(await license.ValidateAsync()).Should().BeTrue();
		(await license.ValidateAsync()).Should().BeTrue();
		(await license.ValidateAsync()).Should().BeTrue();

		license.Token.Claims.First(c => c.Type == "foo").Value.Should().Be("bar");
		license.HasEntitlement("my_entitlement").Should().BeTrue();
		license.HasEntitlements(["my_entitlement", "missing_entitlement"], out var missing).Should().BeFalse();
		missing.Should().Be("missing_entitlement");
	}

	[Fact]
	public async Task detects_incorrect_public_key() {
		var (publicKey2, _) = CreateKeyPair();

		var license = await License.CreateAsync(new Dictionary<string, object> {
			{ "foo", "bar" }
		});

		(await license.ValidateAsync(publicKey2)).Should().BeFalse();
	}

	[Fact]
	public async Task cannot_create_with_inconsistent_keys() {
		var (publicKey, _) = CreateKeyPair();
		var (_, privateKey) = CreateKeyPair();

		Func<Task> act = () => License.CreateAsync(new Dictionary<string, object> {
			{ "foo", "bar" }
		}, publicKey, privateKey);

		await act.Should().ThrowAsync<Exception>().WithMessage("Token could not be validated");
	}
}
