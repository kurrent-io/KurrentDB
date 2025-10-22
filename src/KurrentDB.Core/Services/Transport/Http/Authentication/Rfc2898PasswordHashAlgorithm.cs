// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Security.Cryptography;
using KurrentDB.Core.Authentication.InternalAuthentication;

namespace KurrentDB.Core.Services.Transport.Http.Authentication;

public class Rfc2898PasswordHashAlgorithm : PasswordHashAlgorithm {
	private const int HashSize = 20;
	private const int SaltSize = 16;
	private const int Iterations = 1000;

	public override void Hash(string password, out string hash, out string salt) {
		var saltData = new byte[SaltSize];
		RandomNumberGenerator.Fill(saltData);

		var hashData = Rfc2898DeriveBytes.Pbkdf2(password, saltData, Iterations, HashAlgorithmName.SHA1, HashSize);
		hash = System.Convert.ToBase64String(hashData);
		salt = System.Convert.ToBase64String(saltData);
	}

	public override bool Verify(string password, string hash, string salt) {
		var saltData = System.Convert.FromBase64String(salt);

		var bytes = Rfc2898DeriveBytes.Pbkdf2(password, saltData, Iterations, HashAlgorithmName.SHA1, HashSize);
		var newHash = System.Convert.ToBase64String(bytes);

		return hash == newHash;
	}
}
