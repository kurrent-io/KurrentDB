// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

namespace EventStore.Security.EncryptionAtRest.Transforms.AesGcm;

public static class AesGcmHeader {
	private const int VersionSize = 1; // byte
	private const int AesKeySize = 1;

	public const byte CurrentVersion = 1;
	public const int Size = 32;

	private enum KeySize {
		None = 0,
		Aes128 = 1,
		Aes192 = 2,
		Aes256 = 3
	}

	public static void Write(Span<byte> header, byte version, int aesKeySizeInBits) {
		header[0] = version;
		header = header[VersionSize..];

		var keySize = aesKeySizeInBits switch {
			128 => KeySize.Aes128,
			192 => KeySize.Aes192,
			256 => KeySize.Aes256,
			_ => throw new ArgumentOutOfRangeException(nameof(aesKeySizeInBits), $"Key size not supported: {aesKeySizeInBits}")
		};

		header[..AesKeySize][0] = (byte)keySize;
	}

	public static void Read(ReadOnlySpan<byte> header, out byte version, out int aesKeySizeInBits) {
		version = header[0];
		header = header[VersionSize..];

		var keySize = (KeySize) header[..AesKeySize][0];
		aesKeySizeInBits = keySize switch {
			KeySize.Aes128 => 128,
			KeySize.Aes192 => 192,
			KeySize.Aes256 => 256,
			_ => throw new ArgumentOutOfRangeException(nameof(keySize), $"Unexpected key size value: {keySize}")
		};
	}
}
