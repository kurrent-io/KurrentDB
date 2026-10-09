// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable

namespace KurrentDB.Security.EncryptionAtRest;

public class EncryptionAtRestOptions {
	public bool Enabled { get; set; }

	public MasterKeyOptions MasterKey { get; set; } = new();

	public EncryptionOptions Encryption { get; set; } = new();

	public class EncryptionOptions {
		public AesGcmOptions AesGcm { get; set; } = new();
	}

	public class MasterKeyOptions {
		public FileConfiguratorOptions? File { get; set; }
	}

	public class FileConfiguratorOptions {
		public string KeyPath { get; set; } = "";
	}

	public class AesGcmOptions {
		public bool Enabled { get; set; }
		public int KeySize { get; set; } = 256;
	}
}

