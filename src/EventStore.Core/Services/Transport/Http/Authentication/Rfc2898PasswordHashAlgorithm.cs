// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Security.Cryptography;
using EventStore.Core.Authentication.InternalAuthentication;

namespace EventStore.Core.Services.Transport.Http.Authentication;

public class Rfc2898PasswordHashAlgorithm : PasswordHashAlgorithm {
	private const int HashSize = 20;
	private const int SaltSize = 16;
	private const int Iterations = 1000;
	
	public override void Hash(string password, out string hash, out string salt) {
		var saltData = new byte[SaltSize];
		RandomNumberGenerator.Fill(saltData);
		
		using var rfcBytes = new Rfc2898DeriveBytes(password, saltData, Iterations, HashAlgorithmName.SHA1);
		var hashData = rfcBytes.GetBytes(HashSize);
		hash = System.Convert.ToBase64String(hashData);
		salt = System.Convert.ToBase64String(saltData);
	}

	public override bool Verify(string password, string hash, string salt) {
		var saltData = System.Convert.FromBase64String(salt);

		using var rfcBytes = new Rfc2898DeriveBytes(password, saltData, Iterations, HashAlgorithmName.SHA1);
		var newHash = System.Convert.ToBase64String(rfcBytes.GetBytes(HashSize));

		return hash == newHash;
	}
}
