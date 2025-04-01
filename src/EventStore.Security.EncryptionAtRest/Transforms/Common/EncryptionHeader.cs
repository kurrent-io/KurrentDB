// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace EventStore.Security.EncryptionAtRest.Transforms.Common;

public static class EncryptionHeader {
	private const int VersionSize = 1; // byte
	private const int MasterKeyIdSize = 2; // ushort
	private const int SaltSize = 16; // 128 random bits, to have an extremely low probability of reusing the same salt

	public const byte CurrentVersion = 1;
	public const int Size = 32;

	public static void Write(Span<byte> header, byte version, MasterKey masterKey) {
		header[0] = version;
		header = header[VersionSize..];

		var masterKeyIdSpan = header[..MasterKeyIdSize];
		BinaryPrimitives.WriteUInt16LittleEndian(masterKeyIdSpan, masterKey.Id);
		header = header[MasterKeyIdSize..];

		var saltSpan = header[..SaltSize];
		RandomNumberGenerator.Fill(saltSpan);
	}

	public static void Read(ReadOnlySpan<byte> header, out byte version, out ushort masterKeyId, out ReadOnlySpan<byte> salt) {
		version = header[0];
		header = header[VersionSize..];

		masterKeyId = BinaryPrimitives.ReadUInt16LittleEndian(header[..MasterKeyIdSize]);
		header = header[MasterKeyIdSize..];

		salt = header[..SaltSize];
	}
}
