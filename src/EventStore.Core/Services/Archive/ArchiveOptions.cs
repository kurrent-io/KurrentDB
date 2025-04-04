// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Common.Exceptions;

namespace EventStore.Core.Services.Archive;

public class ArchiveOptions {
	public bool Enabled { get; init; } = false;
	public StorageType StorageType { get; init; } = StorageType.Unspecified;
	public FileSystemOptions FileSystem { get; init; } = new();
	public S3Options S3 { get; init; } = new();
	public RetentionOptions RetainAtLeast { get; init; } = new();

	public void Validate() {
		try {
			ValidateImpl();
		} catch (InvalidConfigurationException ex) {
			throw new InvalidConfigurationException($"Archive configuration: {ex.Message}");
		}
	}

	private void ValidateImpl() {
		if (!Enabled)
			return;

		switch (StorageType) {
			case StorageType.Unspecified:
				throw new InvalidConfigurationException("Please specify a StorageType (e.g. S3)");
			case StorageType.FileSystemDevelopmentOnly:
				FileSystem.Validate();
				break;
			case StorageType.S3:
				S3.Validate();
				break;
			default:
				throw new InvalidConfigurationException("Unknown StorageType");
		}

		RetainAtLeast.Validate();
	}
}

public enum StorageType {
	Unspecified,
	// FileSystem is for development only, it likely will not be able to reliably tell when the archiver
	// node has scavenged a chunk and replaced it.
	FileSystemDevelopmentOnly,
	S3,
}

public class FileSystemOptions {
	public string Path { get; init; } = "";

	public void Validate() {
		if (string.IsNullOrEmpty(Path))
			throw new InvalidConfigurationException("Please provide a Path for the FileSystem archive");
	}
}

public class S3Options {
	public string Bucket { get; init; } = "";
	public string Region { get; init; } = "";

	public void Validate() {
		if (string.IsNullOrEmpty(Bucket))
			throw new InvalidConfigurationException("Please provide a Bucket for the S3 archive");

		if (string.IsNullOrEmpty(Region))
			throw new InvalidConfigurationException("Please provide a Region for the S3 archive");
	}
}

// Local chunks are removed after they have passed beyond both criteria, so they
// must both be set to be useful.
public class RetentionOptions {
	public long Days { get; init; } = TimeSpan.MaxValue.Days;
	// number of bytes in the logical log
	public long LogicalBytes { get; init; } = long.MaxValue;

	public void Validate() {
		if (Days == TimeSpan.MaxValue.Days)
			throw new InvalidConfigurationException("Please specify a value for Days to retain");

		if (LogicalBytes == long.MaxValue)
			throw new InvalidConfigurationException("Please specify a value for LogicalBytes to retain");
	}
}
