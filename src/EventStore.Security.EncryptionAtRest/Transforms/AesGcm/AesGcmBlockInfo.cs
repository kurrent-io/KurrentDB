// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Security.EncryptionAtRest.Transforms.AesGcm;

public static class AesGcmBlockInfo {
	public const int BlockSize = 512;
	public const int HeaderSize = 2;
	public const int DataSize = BlockSize - HeaderSize - TagSize;
	public const int TagSize = 14; // so that `DataSize` is aligned to the AES block size: 16 bytes
}
