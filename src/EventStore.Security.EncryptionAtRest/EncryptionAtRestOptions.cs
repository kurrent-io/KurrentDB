// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable

namespace EventStore.Security.EncryptionAtRest;

public class EncryptionAtRestOptions {
	public bool Enabled { get; init; }

	public MasterKeyOptions MasterKey { get; init; } = new();

	public EncryptionOptions Encryption { get; init; } = new();

	public class EncryptionOptions {
		public AesGcmOptions AesGcm { get; init; } = new();
	}

	public class MasterKeyOptions {
		public FileConfiguratorOptions? File { get; init; }
	}

	public class FileConfiguratorOptions {
		public string KeyPath { get; init; } = "";
	}

	public class AesGcmOptions {
		public bool Enabled { get; init; }
		public int KeySize { get; init; } = 256;
	}
}

