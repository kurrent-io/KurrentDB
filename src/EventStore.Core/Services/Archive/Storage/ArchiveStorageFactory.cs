// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Services.Archive.Naming;
using EventStore.Core.Services.Archive.Storage.S3;

namespace EventStore.Core.Services.Archive.Storage;

public static class ArchiveStorageFactory {
	private const string ArchiveCheckpointFile = "archive.chk";

	public static IArchiveStorage Create(ArchiveOptions options, IArchiveNamingStrategy namingStrategy) =>
		options.StorageType switch {
			StorageType.Unspecified => throw new InvalidOperationException("Please specify an Archive StorageType"),
			StorageType.FileSystemDevelopmentOnly => new ArchiveStorage(new FileSystemBlobStorage(options.FileSystem), namingStrategy, ArchiveCheckpointFile),
			StorageType.S3 => new ArchiveStorage(new S3BlobStorage(options.S3), namingStrategy, ArchiveCheckpointFile),
			_ => throw new ArgumentOutOfRangeException(nameof(options.StorageType))
		};
}
