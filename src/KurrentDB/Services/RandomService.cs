// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Security.Cryptography;

namespace KurrentDB.Services;

public static class RandomService {
	public static string GenerateRandomString(int length) {
		var bytes = new byte[length];
		using (var rng = RandomNumberGenerator.Create()) {
			rng.GetBytes(bytes);
		}

		var chars = new char[length];
		for (int i = 0; i < length; i++) {
			chars[i] = GetRandomChar(bytes[i]);
		}

		return new(chars);
	}

	static char GetRandomChar(byte b) {
		if (b < 26) return (char)('a' + b);
		if (b < 52) return (char)('A' + b - 26);
		if (b < 62) return (char)('0' + b - 52);
		if (b < 94) return (char)('!' + b - 62);
		return (char)(' ' + b - 94);
	}
}
